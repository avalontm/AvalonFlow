namespace AvalonFlowRest.Services
{
    public class EmailService
    {
        public void Send(string to, string subject, string body) =>
            Console.WriteLine($"Email enviado a {to} con asunto '{subject}'");
    }
}
