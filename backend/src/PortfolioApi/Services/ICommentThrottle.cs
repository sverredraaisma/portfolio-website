namespace PortfolioApi.Services;

/// Per-user sliding-window cap on comment creation. Defends against verified-
/// account spam (the IP rate limiter only catches the unauthenticated
/// surface; once a user has a session they could otherwise burst comments).
public interface ICommentThrottle
{
    /// Throws AuthFailedException if the user is over the cap. Successful
    /// creates should call <see cref="Record"/> to bump the window.
    void EnsureCanComment(Guid userId);

    /// Records a successful comment create for windowing.
    void Record(Guid userId);
}
