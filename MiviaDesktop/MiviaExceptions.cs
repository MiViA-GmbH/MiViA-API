using System;

namespace MiviaDesktop;

/// <summary>Worth retrying on the next poll tick: 429 (report in-flight cap), 503, timeouts.</summary>
public class MiviaTransientException : Exception
{
    public MiviaTransientException(string message) : base(message) { }
}

/// <summary>Retrying will not help: bad key, foreign job, failed or expired report.</summary>
public class MiviaPermanentException : Exception
{
    public MiviaPermanentException(string message) : base(message) { }
}
