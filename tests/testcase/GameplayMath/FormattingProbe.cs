using System;
using System.Globalization;
using System.IO;
using System.Text;

// Default Single text is gameplay data when EventGroup stores Execution.region.
public class FormattingProbe
{
    public static void Main(string[] args)
    {
        if(args.Length!=2) throw new ArgumentException("Expected raw float inputs and formatted output TSV");
        Run(args[0],args[1],"unspecified");
    }
    public static void Run(string inputPath,string outputPath,string label)
    {
        byte[] bytes=File.ReadAllBytes(inputPath);
        if(bytes.Length%4!=0) throw new InvalidDataException("Truncated float formatting input");
        CultureInfo saved=System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            using(StreamWriter output=new StreamWriter(outputPath,false,new UTF8Encoding(false)))
            {
                output.NewLine="\n";
                foreach(CultureInfo culture in new CultureInfo[]{CultureInfo.InvariantCulture,new CultureInfo("zh-CN")})
                {
                    System.Threading.Thread.CurrentThread.CurrentCulture=culture;
                    for(int offset=0;offset<bytes.Length;offset+=4)
                    {
                        float value=BitConverter.ToSingle(bytes,offset);
                        string encoded=Convert.ToBase64String(Encoding.UTF8.GetBytes(value.ToString()));
                        output.WriteLine((offset/4).ToString(CultureInfo.InvariantCulture)+"\t"+(culture.Name==""?"Invariant":culture.Name)+"\t"+BitConverter.ToInt32(bytes,offset).ToString("X8")+"\t"+encoded);
                    }
                }
            }
        }
        finally { System.Threading.Thread.CurrentThread.CurrentCulture=saved; }
        Console.WriteLine("FORMATTING_CAPTURE_COMPLETE inputs="+(bytes.Length/4));
    }
}
