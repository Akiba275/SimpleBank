using System;
using SimpleBank.Core;

internal sealed class TestWallet : IWalletAdapter
{
    public long Balance { get; private set; }
    public long HistoricalTotalEarnings { get; private set; }

    public TestWallet(long balance, long historicalTotalEarnings)
    {
        if (balance < 0 || historicalTotalEarnings < 0) throw new ArgumentOutOfRangeException();
        Balance = balance;
        HistoricalTotalEarnings = historicalTotalEarnings;
    }

    public bool TryRemove(long amount)
    {
        if (amount < 0 || amount > Balance) return false;
        Balance -= amount;
        return true;
    }

    public bool TryAdd(long amount)
    {
        if (amount < 0 || amount > long.MaxValue - Balance) return false;
        Balance += amount;
        return true;
    }

    public void RestoreBalance(long balance)
    {
        if (balance < 0) throw new ArgumentOutOfRangeException("balance");
        Balance = balance;
    }

    public void AddOrdinaryIncome(long amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException("amount");
        Balance = checked(Balance + amount);
        HistoricalTotalEarnings = checked(HistoricalTotalEarnings + amount);
    }
}

internal sealed class FailingRestoreWallet : IWalletAdapter
{
    public long Balance { get; private set; }
    public long HistoricalTotalEarnings { get { return 0; } }
    public FailingRestoreWallet(long balance) { Balance = balance; }
    public bool TryRemove(long amount)
    {
        if (amount < 0 || amount > Balance) return false;
        Balance -= amount;
        return true;
    }
    public bool TryAdd(long amount)
    {
        if (amount < 0 || amount > long.MaxValue - Balance) return false;
        Balance += amount;
        return true;
    }
    public void RestoreBalance(long balance) { }
}
