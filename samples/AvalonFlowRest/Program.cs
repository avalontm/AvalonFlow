using AvalonFlow;
using AvalonFlow.Rest;
using AvalonFlowRest.Services;

namespace AvalonFlowRest
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Opcional: puedes leer puerto de args o config
            int port = 5000;

            var server = new AvalonRestServer(port);
            // al iniciar el servidor
            AvalonServiceRegistry.RegisterSingleton<EmailService>(new EmailService());

            await server.StartAsync(); // Corre el servidor indefinidamente
        }
    }
}
