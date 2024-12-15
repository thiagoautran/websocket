using System.Net;
using System.Net.WebSockets;
using System.Text;

var builder = WebApplication.CreateSlimBuilder(args);
//builder.WebHost.UseUrls("http://localhost:6969");

var app = builder.Build();

app.UseWebSockets();

var connections = new List<WebSocket>();

app.Map("/ws", async (HttpContext context) => 
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        return;
    }

    var curName = context.Request.Query["name"];

    using var ws = await context.WebSockets.AcceptWebSocketAsync();

    connections.Add(ws);

    await Broadcast($"{curName} joined the room");
    await Broadcast($"{connections.Count} users connected");

    await ReceiveMessage(ws, async (result, buffer) =>
    {
        if (result.MessageType == WebSocketMessageType.Text)
        {
            string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            //await Broadcast(curName + ": " + message);
            await SendMessage(ws, curName + ": " + message);
        }
        else if (result.MessageType == WebSocketMessageType.Close || ws.State == WebSocketState.Aborted)
        {
            if (connections.Exists(w => w == ws))
                connections.Remove(ws);

            await Broadcast($"{curName} left the room");
            await Broadcast($"{connections.Count} users connected");
            await ws.CloseAsync(result.CloseStatus!.Value, result.CloseStatusDescription, CancellationToken.None);
        }
    });
});

async Task ReceiveMessage(WebSocket socket, Action<WebSocketReceiveResult, byte[]> handleMessage)
{
    var buffer = new byte[1024 * 4];

    try
    {
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            handleMessage(result, buffer);
        }
    }
    catch (Exception)
    {
        if (socket.State != WebSocketState.Open && connections.Exists(ws => ws == socket))
            connections.Remove(socket);
        else
            throw;
    }
}

async Task Broadcast(string message)
{
    var bytes = Encoding.UTF8.GetBytes(message);
    foreach (var socket in connections)
    {
        if (socket.State != WebSocketState.Open && connections.Exists(ws => ws == socket))
        {
            connections.Remove(socket);
            continue;
        }

        var arraySegment = new ArraySegment<byte>(bytes, 0, bytes.Length);
        await socket.SendAsync(arraySegment, WebSocketMessageType.Text, true, CancellationToken.None);
    }
}

async Task SendMessage(WebSocket socket, string message)
{
    if (socket.State != WebSocketState.Open)
        return;

    var bytes = Encoding.UTF8.GetBytes(message);
    var arraySegment = new ArraySegment<byte>(bytes, 0, bytes.Length);
    await socket.SendAsync(arraySegment, WebSocketMessageType.Text, true, CancellationToken.None);
}

await app.RunAsync();