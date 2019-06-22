using System;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using TrackdocDbEntityFramework;

namespace AudioPurge
{
    /// <summary>
    /// Command line program for purging audio. Useful for when there are thousands of audio files to be purged all at once.
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Beginning...");
            Console.WriteLine();
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            Execute(stopwatch);

            stopwatch.Stop();
            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
        }

        private static void Execute(Stopwatch stopwatch)
        {
            int audioPurgeCutoffInDays;
            int.TryParse(ConfigurationManager.AppSettings["audioPurgeCutoffInDays"], out audioPurgeCutoffInDays);

            if (audioPurgeCutoffInDays <= 0)
            {
                throw new Exception("The audioPurgeCutoffInDays appSetting must be set to a positive integer.");
            }

            int count = 0;
            var db = new trackdocEntities();
            var unprocessedRecordIds = Shared.GetUnpurgedAudioIds(db, audioPurgeCutoffInDays);
            int? lastProcessedId = null;

            while (unprocessedRecordIds.Any())
            {
                foreach (var unprocessedRecordId in unprocessedRecordIds)
                {
                    count++;
                    Shared.PurgeAudioRecord(unprocessedRecordId, db);
                    lastProcessedId = unprocessedRecordId;
                    Console.Title = GetStatusString(stopwatch, count);
                }

                db.Dispose();
                db = new trackdocEntities();
                unprocessedRecordIds = Shared.GetUnpurgedAudioIds(db, audioPurgeCutoffInDays, lastProcessedId);
            }

            db.Dispose();
            Console.WriteLine();
            Console.WriteLine(GetStatusString(stopwatch, count));
        }

        private static string GetFormattedTime(Stopwatch stopwatch)
        {
            var elapsed = stopwatch.Elapsed;
            return Math.Floor(elapsed.TotalHours) + "h " + elapsed.Minutes + "m " + elapsed.Seconds + "s";
        }

        private static string GetStatusString(Stopwatch stopwatch, int count)
        {
            string rateString = null;

            if (count != 0)
            {
                var rate = stopwatch.Elapsed.TotalSeconds / count;
                rateString = " (" + string.Format("{0:0.000}", rate) + " seconds/record)";
            }

            return count + " in " + GetFormattedTime(stopwatch) + rateString;
        }
    }
}
