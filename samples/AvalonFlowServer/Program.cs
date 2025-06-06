using AvalonFlow;
using AvalonFlow.Websocket;

namespace SocketFlowConsole
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var cts = new CancellationTokenSource();

            var server = new AvalonWebSocketServer(5000, "ws");

            Console.CancelKeyPress += (s, e) =>
            {
                Console.WriteLine("Stopping server...");
                cts.Cancel();
                e.Cancel = true;
            };
                
            await server.StartAsync(cts.Token);

            while (true) ;
        }
    }
}
