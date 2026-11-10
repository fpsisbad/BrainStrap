using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace BrainStrap.Core;

/// <summary>
/// Optional Discord "Playing Roblox" status. It talks to the Discord desktop app over its local named pipe
/// (discord-ipc-0..9) — nothing is sent anywhere else. Off by default. Only the game name (public information
/// looked up by place id) and the start time are shown; no username, no join button, no account data.
/// </summary>
internal sealed class DiscordRpc : IDisposable
{
    readonly string clientId;
    NamedPipeClientStream? pipe;

    public DiscordRpc(string clientId) => this.clientId = clientId;

    public async Task<bool> ConnectAsync()
    {
        for (var i = 0; i < 10 && pipe == null; i++)
        {
            var p = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                using var cts = new CancellationTokenSource(600);
                await p.ConnectAsync(cts.Token);
                pipe = p;
            }
            catch
            {
                p.Dispose();
            }
        }
        if (pipe == null) return false;

        await SendAsync(0, new JsonObject { ["v"] = 1, ["client_id"] = clientId });
        return await ReadAsync() != null;
    }

    public async Task SetActivityAsync(string details, string state, long startUnix, string? largeImageKey)
    {
        var activity = new JsonObject
        {
            ["details"] = details,
            ["state"] = state,
            ["timestamps"] = new JsonObject { ["start"] = startUnix },
        };
        if (!string.IsNullOrWhiteSpace(largeImageKey))
            activity["assets"] = new JsonObject { ["large_image"] = largeImageKey, ["large_text"] = "BrainStrap" };

        await SendAsync(1, new JsonObject
        {
            ["cmd"] = "SET_ACTIVITY",
            ["args"] = new JsonObject { ["pid"] = Environment.ProcessId, ["activity"] = activity },
            ["nonce"] = Guid.NewGuid().ToString(),
        });
        await ReadAsync();
    }

    public async Task ClearAsync()
    {
        if (pipe == null) return;
        try
        {
            await SendAsync(1, new JsonObject
            {
                ["cmd"] = "SET_ACTIVITY",
                ["args"] = new JsonObject { ["pid"] = Environment.ProcessId },
                ["nonce"] = Guid.NewGuid().ToString(),
            });
        }
        catch
        {
        }
    }

    async Task SendAsync(int op, JsonObject payload)
    {
        var body = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var frame = new byte[8 + body.Length];
        BitConverter.GetBytes(op).CopyTo(frame, 0);
        BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
        body.CopyTo(frame, 8);
        await pipe!.WriteAsync(frame);
        await pipe.FlushAsync();
    }

    async Task<string?> ReadAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(2000);
            var header = new byte[8];
            await ReadExactAsync(header, cts.Token);
            var len = BitConverter.ToInt32(header, 4);
            if (len < 0 || len > 1 << 20) return null;
            var body = new byte[len];
            await ReadExactAsync(body, cts.Token);
            return Encoding.UTF8.GetString(body);
        }
        catch
        {
            return null;
        }
    }

    async Task ReadExactAsync(byte[] buf, CancellationToken ct)
    {
        var got = 0;
        while (got < buf.Length)
        {
            var n = await pipe!.ReadAsync(buf.AsMemory(got), ct);
            if (n == 0) throw new EndOfStreamException();
            got += n;
        }
    }

    public void Dispose() => pipe?.Dispose();
}

internal static class DiscordPresence
{
    /// <summary>Shows the status while Roblox runs, then clears it. Returns when Roblox exits.</summary>
    public static async Task RunAsync(Process roblox, string? placeId, DiscordSettings d)
    {
        string? game = null;
        if (d.ShowGameName && placeId != null) game = await RobloxInstall.GameNameAsync(placeId);

        using var rpc = new DiscordRpc(d.ClientId.Trim());
        if (!await rpc.ConnectAsync())
        {
            Logger.Info("Discord is not running; skipping presence");
            await roblox.WaitForExitAsync();
            return;
        }

        await rpc.SetActivityAsync(game ?? "Playing Roblox", "via BrainStrap", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), d.LargeImageKey);
        await roblox.WaitForExitAsync();
        await rpc.ClearAsync();
    }
}
