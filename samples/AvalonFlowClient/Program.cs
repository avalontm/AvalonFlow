using AvalonFlow.Websocket;
using AvalonFlow;

namespace AvalonFlowClient
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            string token = AvalonFlowInstance.GenerateToken("avalontm21@gmail.com");

            var client = new AvalonWebSocketClient();
            await client.ConnectAsync(new Uri("ws://localhost:5000/ws"), token);

            while (client.IsConnected)
            {
                await Task.Delay(10);  
                Console.Write("> ");
                string? input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                try
                {
                    // Envía el mensaje con la acción "chatMessage"
                    await client.SendMessageAsync("chatMessage", new { message = input });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error enviando mensaje: {ex.Message}");
                }
            }

            // Desconectar antes de salir (suponiendo que tienes DisconnectAsync)
            await client.DisconnectAsync();
        }

    }
}
