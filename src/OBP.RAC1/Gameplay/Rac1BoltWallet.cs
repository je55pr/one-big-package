namespace OBP.RAC1.Gameplay;

/// <summary>The 0x2a6b70 resource credit, independently owned from level-local crates.</summary>
public sealed class Rac1BoltWallet
{
    public int Balance { get; private set; }
    public Rac1BoltWallet(int balance = 0)
    {
        if (balance < 0) throw new ArgumentOutOfRangeException(nameof(balance));
        Balance = balance;
    }
    public void Credit(int value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        Balance = checked(Balance + value);
    }
}
