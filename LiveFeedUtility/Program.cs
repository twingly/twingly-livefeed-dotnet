using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml;

namespace LiveFeedUtility
{
    class Program
    {
        static void Main(string[] args)
        {
            var client = new Client();

            client.Main(args);
        }
    }

    public class Client
    {
        private const int MaxNoOfPostsDefault = 100;

        public bool ShouldQuit { get; set; }

        private int _numberOfPostsSinceStartup = 0;

        public void Main(string[] args)
        {
            if (args.Length < 3)
            {
                Usage();
                return;
            }
            var apiKey = args[0];
            var from = ParseDateTime(args[1]);
            var to = ParseDateTime(args[2]);
            var numberOfPosts = MaxNoOfPostsDefault;
            if (args.Length == 4)
            {
                if (! int.TryParse(args[3], out numberOfPosts) || numberOfPosts < 1)
                {
                    Console.WriteLine("Bad value for parameter maximum number of posts: {0}", args[4]);
                    Usage();
                    return;
                }
            }
            if (from.HasValue && to.HasValue && ! string.IsNullOrEmpty(apiKey) && from.Value < to.Value)
            {
                Run(apiKey, from.Value, to.Value, numberOfPosts);
            } else
            {
                Console.WriteLine("Error parsing parameters. Make sure you stated timestamps in the given format, and that from < to");
                Usage();
                return;
            }
        }

        private void Usage()
        {
            Console.WriteLine("");
            Console.WriteLine("program.exe \"<apikey>\" \"<from timestamp>\" \"<to timestamp>\" [<maximum number of posts>]");
            Console.WriteLine("   timestamps should be given in the format: '{0:u}'>\"", DateTime.Now);
            Console.WriteLine("   maximum number of posts is optional - it defaults to {0}", MaxNoOfPostsDefault);
        }

        public DateTime? ParseDateTime(string raw)
        {
            try
            {
                Console.WriteLine("trying to parse '{0}' as a datetime", raw);
                var from = DateTime.ParseExact(raw, "u", System.Globalization.CultureInfo.InvariantCulture);
                return from;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error parsing datetime: error " + ex.Message);
                return null;
            }
        }

        private void Run(string apiKey, DateTime pFrom, DateTime pTo, int pNumberOfPosts)
        {
            var client = new LiveFeedServiceReference.LiveFeed2SoapClient();

            var programStartTimestamp = DateTime.Now.ToUniversalTime();
            Console.WriteLine("Program started: " + programStartTimestamp);

            pFrom = pFrom.ToUniversalTime();
            pTo = pTo.ToUniversalTime();

            DateTime from = pFrom;
            while (true)
            {
                // Check if we should quit
                if (ShouldQuit)
                    return;

                from = from.ToUniversalTime();
                var to = pTo.ToUniversalTime();

                var requestStopwatch = Stopwatch.StartNew();
                try
                {
                    Console.WriteLine("{0:u} - {1:u} - Trying to fetch data", from, to);
                    //fetch the data
                    XmlNode data = client.GetDataByPostCountAndTimespan(
                        apiKey,
                        from,
                        to,
                        pNumberOfPosts);

                    //parse the result and do something with it 
                    DoSomethingWithResults(from, data);

                    var numberOfPosts = int.Parse(data.Attributes["noOfPosts"].InnerText);
                    _numberOfPostsSinceStartup += numberOfPosts;

                    Console.WriteLine("{0:u} - {1:u} - got {2} posts - total number of posts: {3}", from, to, numberOfPosts, _numberOfPostsSinceStartup);
                    if (numberOfPosts == 0)
                    {
                        // If we didn't get any posts in the specified timeinterval, we are finished
                        Console.WriteLine("Finished: total number of posts {0}", _numberOfPostsSinceStartup);
                        return;
                    }
                    else
                    {
                        // We got some posts, so lastPost and lastPostMs attributes should exist
                        // Use them to update the from parameter, and add 1 millisecond, 
                        // so we don't refetech the ones we already have fetched.
                        var lastPostTs = DateTime.
                            Parse(data.Attributes["lastPost"].InnerText).
                            ToUniversalTime();

                        var lastPostMs = Double.
                            Parse(data.Attributes["lastPostMs"].InnerText);

                        from = lastPostTs.AddMilliseconds(lastPostMs + 1);
                    }
                }
                catch (Exception ex)
                {
                    // Log the error
                    Console.WriteLine("Got some kind of exception.. " + ex.Message);
                    Console.WriteLine("Will retry in 30 seconds");
                    // Wait a bit and try again.
                    Thread.Sleep(30 * 1000);
                }
            }
        }

        private void DoSomethingWithResults(DateTime from, XmlNode document)
        {
            var filename = GetFilename(from);
            try
            {
                var xmlWriter = XmlWriter.Create(filename);
                try
                {
                    ((XmlElement) document).WriteTo(xmlWriter);
                } finally
                {
                    xmlWriter.Close();
                }
            } 
            catch (Exception ex)
            {
                Console.WriteLine("Error writing to output file {0}. Error: {1}", filename, ex.Message);
            }
        }

        private string GetFilename(DateTime from)
        {

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                GetSafeFilename(from.ToUniversalTime().ToString("u"), "_") + ".xml");
        }

        public static string GetSafeFilename(string unsafeFilename, string charReplacer)
        {
            var invalidChars = Path.GetInvalidFileNameChars();

            Array.ForEach(Path.GetInvalidFileNameChars(),
                c => unsafeFilename = unsafeFilename.Replace(c.ToString(), charReplacer));

            return unsafeFilename;
        }
   }
}