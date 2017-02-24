﻿using AlephNote.PluginInterface;
using System;
using System.Collections.Generic;
using System.Linq;
using MSHC.Lang.Collections;

namespace AlephNote.Plugins.SimpleNote
{
	/// <summary>
	/// https://simperium.com/docs/reference/http/
	/// </summary>
	static class SimpleNoteAPI
	{
		private static readonly DateTimeOffset TIMESTAMP_ORIGIN = new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0));

#pragma warning disable 0649
		// ReSharper disable All
		public class APIResultAuthorize { public string username, access_token, userid; }
		public class APIResultIndex { public string current, mark; public List<APIResultIndexObj> index = new List<APIResultIndexObj>(); }
		public class APIResultIndexObj { public string id; public int v; }
		public class APIResultNoteData { public List<string> tags = new List<string>(); public bool deleted; public string shareURL, content, publishURL; public List<string> systemTags = new List<string>(); public double modificationDate, creationDate; }
		public class APISendNoteData { public List<string> tags = new List<string>(); public string content; }
		public class APIDeleteNoteData { public bool deleted; }
		public class APISendAuth { public string username, password; }
		// ReSharper restore All
#pragma warning restore 0649

		public static APIResultAuthorize Authenticate(ISimpleJsonRest web, string userName, string password)
		{
			return web.PostTwoWay<APIResultAuthorize>(new APISendAuth {username = userName, password = password}, "authorize/");
		}

		public static APIResultIndex ListBuckets(ISimpleJsonRest web)
		{
			var idx = web.Get<APIResultIndex>("note/index");

			while (!string.IsNullOrWhiteSpace(idx.mark))
			{
				var idx2 = web.Get<APIResultIndex>("note/index");

				idx.current = idx2.current;
				idx.mark = idx2.mark;
				idx.index.AddRange(idx2.index);
			}

			return idx;
		}

		public static SimpleNote GetNoteData(ISimpleJsonRest web, string noteID, SimpleNoteConfig cfg, int? version = null)
		{
			if (version != null)
				return GetNoteFromQuery(web.Get<APIResultNoteData>(string.Format("note/i/{0}/v/{1}", noteID, version)), web, noteID, cfg);
			else
				return GetNoteFromQuery(web.Get<APIResultNoteData>("note/i/" + noteID), web, noteID, cfg);
		}

		public static SimpleNote UploadNewNote(ISimpleJsonRest web, SimpleNote note, SimpleNoteConfig cfg)
		{
			note.Deleted = false;
			note.CreationDate = DateTimeOffset.Now;
			note.ModificationDate = DateTimeOffset.Now;
			
			APIResultNoteData data = new APIResultNoteData
			{
				tags = note.Tags.ToList(),
				deleted = false,
				shareURL = note.ShareURL,
				publishURL = note.PublicURL,
				systemTags = note.SystemTags,
				content = note.Content,
				creationDate = ConvertToEpochDate(note.CreationDate),
				modificationDate = ConvertToEpochDate(note.ModificationDate),
			};
			
			var r = web.PostTwoWay<APIResultNoteData>(data, "note/i/" +note.ID, "response=1");

			return GetNoteFromQuery(r, web, note.ID, cfg);
		}

		public static SimpleNote ChangeExistingNote(ISimpleJsonRest web, SimpleNote note, SimpleNoteConfig cfg, out bool updated)
		{
			if (note.Deleted) throw new Exception("Cannot update an already deleted note");
			if (note.ID == "") throw new Exception("Cannot change a not uploaded note");
			note.ModificationDate = DateTimeOffset.Now;
			
			APISendNoteData data = new APISendNoteData
			{
				tags = note.Tags.ToList(),
				content = note.Content,
			};
			
			var r = web.PostTwoWay<APIResultNoteData>(data, "note/i/" + note.ID, new[] {412}, "response=1");

			if (r == null)
			{
				// Statuscode 412 - Empty change

				updated = false;
				return (SimpleNote)note.Clone();
			}

			updated = true;
			return GetNoteFromQuery(r, web, note.ID, cfg);
		}

		public static void DeleteNotePermanently(ISimpleJsonRest web, SimpleNote note)
		{
			if (note.ID == "") throw new SimpleNoteAPIException("Cannot delete a not uploaded note");

			note.ModificationDate = DateTimeOffset.Now;
			web.DeleteEmpty("note/i/" + note.ID);
		}

		public static void DeleteNote(ISimpleJsonRest web, SimpleNote note)
		{
			if (note.ID == "") throw new Exception("Cannot delete a not uploaded note");
			note.ModificationDate = DateTimeOffset.Now;
			
			APIDeleteNoteData data = new APIDeleteNoteData
			{
				deleted = true
			};
			
			web.PostUpload(data, "note/i/" + note.ID, new[] { 412 });
		}

		private static SimpleNote GetNoteFromQuery(APIResultNoteData r, ISimpleJsonRest c, string id, SimpleNoteConfig cfg)
		{
			try
			{
				var n = new SimpleNote(id, cfg)
				{
					Deleted = r.deleted,
					ShareURL = r.shareURL,
					PublicURL = r.publishURL,
					SystemTags = r.systemTags,
					Content = r.content,
					ModificationDate = ConvertFromEpochDate(r.modificationDate),
					CreationDate = ConvertFromEpochDate(r.creationDate),
					LocalVersion = int.Parse(c.GetResponseHeader("X-Simperium-Version")),
				};

				n.Tags.Synchronize(r.tags);

				return n;
			}
			catch (Exception e)
			{
				throw new SimpleNoteAPIException("SimpleNote API returned unexpected note data", e);
			}
		}

		private static DateTimeOffset ConvertFromEpochDate(double seconds)
		{
			if (seconds <= 0) return TIMESTAMP_ORIGIN;

			return TIMESTAMP_ORIGIN.AddSeconds(seconds);
		}

		private static double ConvertToEpochDate(DateTimeOffset offset)
		{
			return TimeZoneInfo.ConvertTimeToUtc(offset.DateTime, TimeZoneInfo.Local).ToUniversalTime().Subtract(TIMESTAMP_ORIGIN.DateTime).TotalSeconds;
		}
	}
}
