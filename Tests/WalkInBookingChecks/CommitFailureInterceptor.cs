using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;

// Simulates a database failure at commit, after SaveChanges has succeeded.
sealed class CommitFailureInterceptor(IHttpContextAccessor accessor) : DbTransactionInterceptor
{
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (accessor.HttpContext?.Request.Headers.ContainsKey("X-Test-Fail-Commit") == true)
            throw new InjectedCommitException();
        return ValueTask.FromResult(result);
    }

    private sealed class InjectedCommitException : DbException
    {
        public InjectedCommitException() : base("Injected commit failure") { }
    }
}
