using System;
using System.Linq;
using TrackdocDbEntityFramework;

namespace AudioPurge
{
    /// <summary>
    /// Class for code shared between AudioPurge command line program and Audio Processor Service.
    /// </summary>
    public class Shared
    {
        /// <summary>
        /// Returns an array of a maximum of 10 ordered ints corresponding to the oldest non-purged audio records older than the 
        /// given cutoffDays. If startAfterId is given and not null, it is used as an additional filter in the WHERE clause to 
        /// improve performance.
        /// </summary>
        /// <param name="db"></param>
        /// <param name="cutoffDays"></param>
        /// <param name="startAfterId"></param>
        /// <returns></returns>
        public static int[] GetUnpurgedAudioIds(trackdocEntities db, int cutoffDays, int? startAfterId = null)
        {
            var cutoff = DateTime.Now - TimeSpan.FromDays(cutoffDays);
            var audiosToPurge = db.audios.Where(a => a.purged != true && a.date_created < cutoff);
            if (startAfterId != null) { audiosToPurge = audiosToPurge.Where(a => a.id > startAfterId); }

            return audiosToPurge.OrderBy(a => a.id)
                .Select(a => a.id)
                .Take(10)// 10 seems to be significantly faster than higher values like 100 or 1000 for some reason.
                .ToArray();
        }

        /// <summary>
        /// Updates the audio record in the database corresponding to the given audioRecordId so its audio_lob is set to an 
        /// empty byte array. Purged is set to true. The date_last_modified and last_modified_by properties are also set.
        /// </summary>
        /// <param name="audioRecordId"></param>
        /// <param name="db"></param>
        public static void PurgeAudioRecord(int audioRecordId, trackdocEntities db)
        {
            var exsitingAudioToPurge = new audio();
            exsitingAudioToPurge.id = audioRecordId;
            exsitingAudioToPurge.audio_lob = new byte[0];
            exsitingAudioToPurge.purged = true;
            exsitingAudioToPurge.date_last_modified = DateTime.Now;
            exsitingAudioToPurge.last_modified_by = 0;

            db.audios.Attach(exsitingAudioToPurge);
            db.Entry(exsitingAudioToPurge).Property(d => d.purged).IsModified = true;
            db.Entry(exsitingAudioToPurge).Property(d => d.audio_lob).IsModified = true;
            db.Entry(exsitingAudioToPurge).Property(d => d.date_last_modified).IsModified = true;
            db.Entry(exsitingAudioToPurge).Property(d => d.last_modified_by).IsModified = true;

            db.Configuration.ValidateOnSaveEnabled = false;// Needed because some of the properties we're ignoring are required.
            db.SaveChanges();
        }
    }
}
