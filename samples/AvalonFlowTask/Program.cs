using AvalonFlow;
using System.Threading;

namespace AvalonFlowConsole
{
    class Program
    {
        private static int _folioCounter = 1;
        private static readonly object _lock = new();

        static async Task Main()
        {
            var queue = new AvalonFlowQueueService<FlowJob>(
                maxSeconds: 10,
                autoStart: true,
                onLog: msg => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}")
            );

            // Encolar trabajos iniciales
            for (int i = 0; i < 5; i++)
            {
                await queue.Enqueue("folio", CreateFolioJob());
            }

            // Iniciar procesamiento solo una vez (autoStart: true lo hace también)
            _ = queue.StartProcessing("folio");

            Console.WriteLine("Processing started. Press 'a' to add job, 'q' to quit.");

            while (true)
            {
                var key = Console.ReadKey(true).Key;

                if (key == ConsoleKey.A)
                {
                    await queue.Enqueue("folio", CreateFolioJob());
                    Console.WriteLine("→ Job added.");
                }
                else if (key == ConsoleKey.Q)
                {
                    queue.CancelProcessing("folio");
                    break;
                }
            }

            Console.WriteLine("Exiting...");
        }


        static FlowJob CreateFolioJob()
        {
            return new FlowJob
            {
                Key = "folio",
                Work = async (cancellationToken) =>
                {
                    await OnProcess(cancellationToken);
                }
            };
        }

        private static async Task OnProcess(CancellationToken cancellationToken)
        {
            // Simula trabajo con soporte para cancelación
            await Task.Delay(1000, cancellationToken);

            string folio;
            lock (_lock)
            {
                folio = _folioCounter.ToString("D4");
                _folioCounter++;
            }

            Console.WriteLine($"→ Folio generado: {folio}");
        }
    }
}
