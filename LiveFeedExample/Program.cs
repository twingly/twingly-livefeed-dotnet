using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml;

namespace LiveFeedExample
{
    class Program
    {
        static void Main(string[] args)
        {
            var client = new Client();

            // Create a new thread to run the background job in, so that the main thread can wait for console input
            var t = new Thread(client.LivefeedExample);
            // Start the thread
            t.Start();

            // Wait for user to hit enter
            Console.ReadLine();

            // Tell the thread to stop executing
            client.ShouldQuit = true;
            // Wait until it is finished
            t.Join();
        }
    }

    public class Client
    {
        private const string ApiKey = "your-api-key";
        private const int MaxNoOfPosts = 1000;

        public bool ShouldQuit { get; set; }

        private int _numberOfPostsSinceStartup = 0;

        public void LivefeedExample()
        {
            var client = new LiveFeedServiceReference.LiveFeed2SoapClient();

            var programStartTimestamp = DateTime.Now.ToUniversalTime();
            Console.WriteLine("Program started: " + programStartTimestamp);

            while (true)
            {
                // Check if we should quit
                if (ShouldQuit)
                    return;

                // Initialize the from and to timestamps. 
                // The from-parameter is read from disk, if the file exists, 
                // otherwise it gets set to a default value of now minus 1 hour
                var from = ReadFromTimestampFromDisk();
                // The to-parameter is always set to now minus 5 minutes 
                // (5 minutes is chosen so the code works even if clocks 
                // are somewhat out-of-sync)
                var to = DateTime.Now.ToUniversalTime().AddMinutes(-5); 

                var requestStopwatch = Stopwatch.StartNew();
                try
                {
                    //fetch the data
                    XmlNode data = client.GetDataByPostCountAndTimespan(
                        ApiKey,
                        from,
                        to,
                        MaxNoOfPosts);

                    //parse the result and do something with it 
                    DoSomethingWithResults(data);

                    var numberOfPosts = int.Parse(data.Attributes["noOfPosts"].InnerText);
                    if (numberOfPosts == 0)
                    {
                        // If we didn't get any posts in the specified timeinterval, we set from to to and save it
                        from = to;
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
                    WriteFromTimestampToDisk(from);

                    if (numberOfPosts >= MaxNoOfPosts)
                    {
                        // If we get max number of posts or more, we know there might be more to fetch, so no need to sleep.
                        Console.WriteLine("There is possibly more data available, trying again immediately");
                    }
                    else
                    {
                        var milliSecondsToSleep = 4 * 60 * 1000; //sleep 4 minutes
                        requestStopwatch.Stop();
                        // Adjust the time to sleep based on how long it took to fetch and process the last batch
                        milliSecondsToSleep = milliSecondsToSleep - (int)requestStopwatch.Elapsed.TotalMilliseconds;
                        if (milliSecondsToSleep > 0)
                        {
                            Console.WriteLine(
                                "Have fetched {0} posts between {1} and {2} since startup. Going to sleep for {3} ms.",
                                _numberOfPostsSinceStartup, programStartTimestamp, to, milliSecondsToSleep);
                            Thread.Sleep(milliSecondsToSleep);
                        }
                    }
                } 
                catch (Exception ex)
                {
                    // Log the error
                    Console.WriteLine("Got some kind of exception.. " + ex.Message);
                    // Wait one minute and try again.
                    Thread.Sleep(60*1000);
                }
            }
        }

        private void DoSomethingWithResults(XmlNode document)
        {
            int posts = 0;
            foreach (XmlNode node in document.SelectNodes("post/url"))
            {
                posts += 1;
            }
            Console.WriteLine("Received " + posts + " posts.");
            _numberOfPostsSinceStartup += posts;
        }

        public DateTime ReadFromTimestampFromDisk()
        {
            string filename = GetFilename();

            if (File.Exists(filename))
            {
                var text = File.ReadAllText(filename);

                return
                    DateTime.ParseExact(text, "o", System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();
            } 
            else
            {
                // No file exists, return a default value that is now minus 60 minutes.
                return DateTime.Now.ToUniversalTime().AddMinutes(-60);
            }
        }

        private string GetFilename()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nextfrom_timestamp.txt");
        }

        public void WriteFromTimestampToDisk(DateTime from)
        {
            File.WriteAllText(GetFilename(), from.ToString("o"));
        }
    }
}
