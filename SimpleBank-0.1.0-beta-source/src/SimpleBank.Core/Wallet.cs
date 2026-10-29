namespace SimpleBank.Core
{
    public interface IWalletAdapter
    {
        long Balance { get; }
        long HistoricalTotalEarnings { get; }
        bool TryRemove(long amount);
        bool TryAdd(long amount);
        void RestoreBalance(long balance);
    }
}
