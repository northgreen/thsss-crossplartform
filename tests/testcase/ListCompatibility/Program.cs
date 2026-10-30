using SlimDX.Compat;
using Shooting;
int checks=0;

void ConcurrentClearStress(bool callback)
{
    var items=Enumerable.Range(0,256).Select(_=>new object()).ToArray();
    var values=new List<object>(256);values.AddRange(items);
    using var cancellation=new CancellationTokenSource();
    using var started=new ManualResetEventSlim();
    Exception workerError=null;
    long writes=0,walks=0,visits=0;
    var worker=new Thread(()=>
    {
        try
        {
            started.Set();
            while(!cancellation.IsCancellationRequested)
            {
                values.Clear();
                foreach(var item in items) values.Add(item);
                writes++;
            }
        }
        catch(Exception error){workerError=error;}
    }){IsBackground=true,Name="list-clear-writer"};
    worker.Start();
    try
    {
        started.Wait();
        var elapsed=System.Diagnostics.Stopwatch.StartNew();
        Action<object> visit=_=>visits++;
        while(elapsed.Elapsed<TimeSpan.FromSeconds(2))
        {
            if(callback) ListCompat.ForEach(values,visit);
            else foreach(var item in ListCompat.LegacyItems(values)) visit(item);
            walks++;
        }
    }
    finally
    {
        cancellation.Cancel();
        if(!worker.Join(TimeSpan.FromSeconds(5))) throw new TimeoutException("List stress writer did not stop");
        if(workerError!=null) throw new InvalidOperationException("List stress writer failed",workerError);
        Console.WriteLine($"STRESS {(callback?"ForEach":"LegacyItems")}: writes={writes}, walks={walks}, visits={visits}");
    }
    if(writes==0||walks==0||visits==0) throw new Exception("List stress did not exercise concurrent iteration");
    checks++;
}

var stressErrors=new List<Exception>();
foreach(bool callback in new[]{false,true})
{
    try { ConcurrentClearStress(callback); }
    catch(Exception error){Console.WriteLine(error);stressErrors.Add(error);}
}
if(stressErrors.Count!=0) throw new AggregateException("Concurrent clear compatibility failed",stressErrors);
void Equal<T>(IEnumerable<T> actual,IEnumerable<T> expected,string name)
{
    if(!actual.SequenceEqual(expected)) throw new Exception(name+": "+string.Join(',',actual));
    checks++;
}
void Reference(List<int> values,Action<int> action)
{
    // Synchronous mutation oracle only; the checked indexer is NOT a concurrent CLR2 oracle.
    for(int index=0;index<values.Count;index++) action(values[index]);
}
foreach(int scenario in Enumerable.Range(0,5))
{
    List<int> Run(int mode)
    {
        var values=new List<int>{1,2,3};var seen=new List<int>();
        void Visit(int item)
        {
            seen.Add(item);
            if(item!=1) return;
            switch(scenario)
            {
                case 0: values.Remove(1);break;
                case 1: values.Remove(2);break;
                case 2: values.Add(4);break;
                case 3: values.Clear();break;
                case 4: values[1]=9;break;
            }
        }
        if(mode==0) Reference(values,Visit);
        else if(mode==1) foreach(int item in ListCompat.LegacyItems(values)) Visit(item);
        else ListCompat.ForEach(values,Visit);
        return seen;
    }
    Equal(Run(1),Run(0),"legacy mutation scenario="+scenario);
    Equal(Run(2),Run(0),"callback mutation scenario="+scenario);
}
Equal(ListCompat.LegacyItems(new System.Collections.ObjectModel.Collection<int>{1,2,3}),new[]{1,2,3},"non-List IList fallback");
void NullArgument(Action action,string parameter)
{
    try { action(); }
    catch(ArgumentNullException error) when(error.ParamName==parameter){checks++;return;}
    throw new Exception("Missing immediate ArgumentNullException: "+parameter);
}
NullArgument(()=>ListCompat.LegacyItems<int>(null),"list");
NullArgument(()=>ListCompat.ForEach<int>(null,_=>{}),"list");
NullArgument(()=>ListCompat.ForEach(new List<int>(),null),"action");
var list=new List<int>{1,2};var visited=new List<int>();
ListCompat.ForEach(list,item=>{visited.Add(item);if(item==1)list.Add(3);});
Equal(visited,new[]{1,2,3},"legacy callback append same pass");
foreach(bool callback in new[]{false,true})
{
    var growing=new List<int>(1){1};
    int originalCapacity=growing.Capacity;
    var seen=new List<int>();
    void Visit(int item)
    {
        seen.Add(item);
        if(item==1){growing.Add(2);growing.Add(3);}
    }
    if(callback) ListCompat.ForEach(growing,Visit);
    else foreach(int item in ListCompat.LegacyItems(growing)) Visit(item);
    if(growing.Capacity<=originalCapacity) throw new Exception("Append test did not reallocate backing array");
    Equal(seen,new[]{1,2,3},$"capacity growth visited same pass callback={callback}");
}
var obj=new BaseObject_CS{EventGroupList=new List<EventGroup>(),EventsExecutionList=new List<Execution>()};
var expiring=new Execution{changevalue=999,ctime=1};var next=new Execution{changevalue=999,ctime=2};
obj.EventsExecutionList.AddRange(new[]{expiring,next});obj.EventCtrl();
if(next.ctime!=2||obj.EventsExecutionList.Count!=1)throw new Exception("Actual event self-removal did not preserve original live-index shift");checks++;
obj.EventCtrl();if(next.ctime!=1)throw new Exception("Remaining event did not run on next frame");checks++;
Console.WriteLine($"PASS: {checks} legacy enumeration and actual event pipeline checks");

using(var release=new ManualResetEventSlim())
{
    var worker=new Thread(()=>release.Wait()){IsBackground=true,Name="list-test-worker"};worker.Start();
    bool timedOut=false;
    try { ThreadCompat.Stop(worker,5); } catch(TimeoutException) { timedOut=true; }
    finally { release.Set(); worker.Join(); }
    if(!timedOut)throw new Exception("Unfinished thread was reported as stopped");checks++;
    ThreadCompat.Stop(worker);
}
Console.WriteLine("PASS: unfinished worker fails explicitly; completed worker joins safely");

var background=new BackgroundManager();
var first=new AuditObject{Outside=true};var shifted=new AuditObject{Outside=true};var last=new AuditObject();
background.BackgroundList.AddRange(new BaseObject[]{first,shifted,last});background.Ctrl();
if(background.BackgroundList.Count!=2||!background.BackgroundList.Contains(shifted)||shifted.Updates!=1)
    throw new Exception("Background removal did not retain legacy shifted-index behavior");
var spawning=new AuditObject();var spawned=new AuditObject();
background.BackgroundList.Clear();background.BackgroundList.Add(spawning);
spawning.OnUpdate=()=>{if(!background.BackgroundList.Contains(spawned))background.BackgroundList.Add(spawned);};
background.Ctrl();if(spawned.Updates!=1)throw new Exception("Appended background was deferred instead of controlled in same legacy pass");
Console.WriteLine("PASS: actual background removal and same-pass spawn ordering");

if(args.Contains("--benchmark"))
{
    var values=Enumerable.Range(0,256).ToList();
    long total=0;
    Action<int> visit=item=>total+=item;
    void Measure(string name,Action walk)
    {
        for(int pass=0;pass<1000;pass++) walk();
        total=0;
        var elapsed=System.Diagnostics.Stopwatch.StartNew();
        for(int pass=0;pass<20000;pass++) walk();
        elapsed.Stop();
        if(total!=652800000) throw new Exception("Benchmark traversal mismatch: "+name);
        Console.WriteLine($"BENCH {name}: {elapsed.Elapsed.TotalMilliseconds:F2} ms / 5,120,000 items; {elapsed.Elapsed.TotalNanoseconds/5120000:F2} ns/item");
    }
    Measure("checked indexer",()=>Reference(values,visit));
    Measure("cached FieldInfo ForEach",()=>ListCompat.ForEach(values,visit));
    Measure("cached FieldInfo LegacyItems",()=>{foreach(int item in ListCompat.LegacyItems(values)) visit(item);});
}

sealed class AuditObject:BaseObject
{
    public bool Outside;public int Updates;public Action OnUpdate;
    public override bool OutBoundary()=>Outside;
    public override void Ctrl(){Updates++;OnUpdate?.Invoke();}
}
