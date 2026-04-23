namespace PortfolioApi.Services;

public interface IEmailService
{
    Task SendVerificationAsync(string toEmail, string jwtToken);
}
