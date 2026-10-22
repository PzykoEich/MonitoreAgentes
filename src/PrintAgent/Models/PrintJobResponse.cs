namespace PrintAgent.Models;

public record PrintJobResponse(
    bool Success,
    string JobId,
    string Message
);