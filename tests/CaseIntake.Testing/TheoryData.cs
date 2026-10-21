using System.Collections;

namespace CaseIntake.Testing;

/// <summary>Typed rows for <c>[TestCaseSource]</c>: each row is passed as the test method's arguments.</summary>
public abstract class TheoryDataBase : IEnumerable<object[]>
{
    private readonly List<object[]> _rows = [];

    // Typed object[] like the rows they replace; an argument can still be null at run time.
    protected void AddRow(params object?[] values) => _rows.Add(values!);

    public IEnumerator<object[]> GetEnumerator() => _rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Single-argument rows. Enumerates the values themselves (NUnit passes each as the one argument), so a collection
/// expression such as <c>[typeof(A), typeof(B)]</c> builds it.
/// </summary>
public class TheoryData<T1> : IEnumerable<T1>
{
    private readonly List<T1> _rows = [];

    public void Add(T1 p1) => _rows.Add(p1);

    public IEnumerator<T1> GetEnumerator() => _rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class TheoryData<T1, T2> : TheoryDataBase
{
    public void Add(T1 p1, T2 p2) => AddRow(p1, p2);
}

public class TheoryData<T1, T2, T3> : TheoryDataBase
{
    public void Add(T1 p1, T2 p2, T3 p3) => AddRow(p1, p2, p3);
}

public class TheoryData<T1, T2, T3, T4> : TheoryDataBase
{
    public void Add(T1 p1, T2 p2, T3 p3, T4 p4) => AddRow(p1, p2, p3, p4);
}

public class TheoryData<T1, T2, T3, T4, T5> : TheoryDataBase
{
    public void Add(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5) => AddRow(p1, p2, p3, p4, p5);
}

public class TheoryData<T1, T2, T3, T4, T5, T6> : TheoryDataBase
{
    public void Add(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6) => AddRow(p1, p2, p3, p4, p5, p6);
}

public class TheoryData<T1, T2, T3, T4, T5, T6, T7> : TheoryDataBase
{
    public void Add(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7) => AddRow(p1, p2, p3, p4, p5, p6, p7);
}

public class TheoryData<T1, T2, T3, T4, T5, T6, T7, T8> : TheoryDataBase
{
    public void Add(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8) => AddRow(p1, p2, p3, p4, p5, p6, p7, p8);
}
