namespace NightsHack.Modifier;
internal sealed record InjectorEvent(string Code, string Message, string Level = "INFO", int TargetProcessId = 0);
