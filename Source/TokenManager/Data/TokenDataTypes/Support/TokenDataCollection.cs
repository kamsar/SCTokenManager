﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Sitecore;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;

namespace TokenManager.Data.TokenDataTypes.Support
{
	public class TokenDataCollection
	{
		private readonly NameValueCollection _source;

		public string[] AllKeys => _source.AllKeys;

		public void Add(string name, string value)
		{
			_source.Add(name, System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value)));
		}

		public void Remove(string name)
		{
			_source.Remove(name);
		}

		public IEnumerable<T> Cast<T>()
		{
			return _source.Cast<T>();
		}
		public string this[string tokenDataName]
		{
			get
			{
				string ret = _source[tokenDataName];
				if (string.IsNullOrWhiteSpace(ret))
					return ret;
				try
				{
					if (ret.StartsWith("TMB64-"))
						return Encoding.UTF8.GetString(System.Convert.FromBase64String(ret.Substring(6)));
					return ret;
				}
				catch (Exception e)
				{
					Log.Error("Unable to process token data field "+tokenDataName +" because it is not valid base64.", e,  this);
					return "";
				}
			}
			set { _source[tokenDataName] = $"TMB64-{System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))}"; }
		}

		public TokenDataCollection()
		{
			_source = new NameValueCollection();
		}
		public TokenDataCollection(NameValueCollection source)
		{
			_source = source;
		}

		public bool GetBoolean(string tokenDataName)
		{
			return this[tokenDataName] == "True";
		}

		public GeneralLink GetLink(string tokenDataName)
		{
			return new GeneralLink(this[tokenDataName]);
		}

		public Item GetItem(string name)
		{
			var db = Context.ContentDatabase ?? Context.Database ?? Factory.GetDatabase("master");

			var value = this[name];

			if (string.IsNullOrWhiteSpace(value)) return null;

			Item item = db?.GetItem(value);

			return item;
		}

		public MediaItem GetMedia(string name)
		{
			var db = Context.ContentDatabase ?? Context.Database ?? Factory.GetDatabase("master");

			var value = this[name];

			if (string.IsNullOrWhiteSpace(value)) return null;

			MediaItem item = db?.GetItem(value);

			return item;
		}

		public int GetInt(string tokenDataName)
		{
			int ret;
			if (int.TryParse(this[tokenDataName], out ret))
			{
				return ret;
			}
			return -1;
		}

		public ID GetId(string tokenDataName)
		{
			string val = this[tokenDataName];
			if (val == null) return null;
			try
			{
				return new ID(this[tokenDataName]);
			}
			catch (FormatException)
			{
				return null;
			}
		}

		public string GetString(string tokenDataName)
		{
			return this[tokenDataName];
		}

		public string GetString(string tokenDataName, string defaultValue)
		{
			var value = this[tokenDataName];

			if (string.IsNullOrEmpty(value)) return defaultValue;

			return value;
		}

		public string GetDropdownValue(string tokenDataName)
		{
			return this[tokenDataName];
		}

		public override string ToString()
		{
			StringBuilder sb = new StringBuilder();
			foreach (string key in _source.Keys)
			{
				sb.Append($"{key}={_source[key]}&");
			}
			sb.Remove(sb.Length - 1, 1);
			return sb.ToString();
		}
	}
}
