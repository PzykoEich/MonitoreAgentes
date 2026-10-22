using System.Reflection;

namespace PrintAgent.Models;
public record PrintJobRequest(
    string Payload,
    string? TargetIp = null,
    int Port = 9100,
    bool IsBase64 = false,
    string? SecurityToken = null
);