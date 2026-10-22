namespace PrintAgent.Models;

public record AgentStatusResponse(
    string Status,
    string Version,
    string MachineName,
    string LocalIP,
    string AssignedPrinterIP,
    DateTime Timestamp
);