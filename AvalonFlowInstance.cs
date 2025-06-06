using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AvalonFlow
{
    public static class AvalonFlowInstance
    {
        public static string JwtSecretKey { get; set; } = "Y6m7D8N3pF1XqVzLsW2RtG9BhJeUkCoPvCyZAxMlQwSbEfHiJrTnKgUdOyElPaVw";


        public static string GenerateJwtToken(string username, string role = "User", DateTime? expires = null)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            if (expires == null)
            {
                expires = DateTime.UtcNow.AddHours(2);
            }
            else if (expires < DateTime.UtcNow)
            {
                throw new ArgumentException("Expiration time must be in the future.");
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, role)
            };

            var token = new JwtSecurityToken(
                issuer: "avalon",
                audience: "avalon-users",
                claims: claims,
                expires: expires,
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public static ClaimsPrincipal? ValidateJwtToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(JwtSecretKey);

            try
            {
                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = "avalon",
                    ValidateAudience = true,
                    ValidAudience = "avalon-users",
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }, out _);

                return principal;
            }
            catch
            {
                return null;
            }
        }

        public static void Log(string message)
        {
            Console.WriteLine($"[{DateTime.Now.ToString("hh:mm:ss")}] {message}");
        }
    }
}
