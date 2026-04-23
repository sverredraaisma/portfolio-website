namespace PortfolioApi.Services;

public interface IEmailService
{
    Task SendVerificationAsync(string toEmail, string jwtToken);
    Task SendPasswordResetAsync(string toEmail, string jwtToken);
}
