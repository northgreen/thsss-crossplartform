namespace Shooting;

public class CounterOwner : BaseObject
{
    static int receivers, indexes, values;
    static CounterReceiver receiver = new();
    static CounterReceiver Receiver() { receivers++; return receiver; }
    static int Index() { indexes++; return 0; }
    static float Value() { values++; return 3; }
    public static string Compound()
    {
        receivers = indexes = values = 0; receiver = new();
        Receiver().Value += Value();
        Receiver()[Index()] *= Value();
        return $"{receivers},{indexes},{values},{receiver.Reads},{receiver.Writes},{receiver.Value},{receiver[0]}";
    }
}
public class CounterReceiver
{
    float property = 2, indexer = 4;
    public int Reads, Writes;
    public float Value { get { Reads++; return property; } set { Writes++; property = value; } }
    public float this[int index] { get { Reads++; return indexer; } set { Writes++; indexer = value; } }
}
