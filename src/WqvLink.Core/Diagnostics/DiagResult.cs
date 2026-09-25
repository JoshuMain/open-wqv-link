namespace WqvLink.Core.Diagnostics;

public enum DiagVerdict
{
    Pass,
    Fail,
    Partial,
    Inconclusive,

    //The test ran and only the user can judge the result (for example Blast)
    Done,
}

//Outcome of a bench test 
public sealed record DiagResult(DiagVerdict Verdict, string Message, string Details = "")
{
    public override string ToString() =>
        Details.Length == 0 ? $"{Verdict.ToString().ToUpperInvariant()}: {Message}" : $"{Verdict.ToString().ToUpperInvariant()}: {Message}\n  {Details}";
}
