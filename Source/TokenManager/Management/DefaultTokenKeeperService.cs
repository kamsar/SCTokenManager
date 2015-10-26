﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Web;
using Sitecore;
using Sitecore.Configuration;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Security;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Globalization;
using Sitecore.Pipelines;
using Sitecore.Pipelines.RenderField;
using TokenManager.ContentSearch;
using TokenManager.Data.Interfaces;
using TokenManager.Pipelines;

namespace TokenManager.Management
{
	class DefaultTokenKeeperService : ITokenKeeperService
	{
		public string TokenPrefix => "<a class=\"token-manager-token\" href=\"/TokenManager?";
		public string TokenSuffix => "</a>";
		public string TokenCss { get; set; }
		private static readonly ConcurrentDictionary<string, ITokenCollection<IToken>> TokenCollections = new ConcurrentDictionary<string, ITokenCollection<IToken>>();
		private static readonly ConcurrentDictionary<string, DateTime> TokenCacheUpdateTimes = new ConcurrentDictionary<string, DateTime>(); 
		private static readonly ConcurrentDictionary<string, Tuple<DateTime, List<Tuple<int, int>>>> TokenLocations = new ConcurrentDictionary<string, Tuple<DateTime, List<Tuple<int, int>>>>();

		public DefaultTokenKeeperService()
		{
		}

		public DefaultTokenKeeperService(string tokenCss)
		{
			TokenCss = tokenCss;
		}

		public ITokenCollection<IToken> this[string tokenCollection]
		{
			get { return GetTokenCollection<IToken>(tokenCollection); }
			set { LoadTokenCollection(value); }
		}

		public virtual void LoadTokenCollection(ITokenCollection<IToken> collection)
		{
			if (collection != null)
			{

				TokenCacheUpdateTimes[collection.GetCollectionLabel()] =
					GetDatabase().GetItem(collection.GetBackingItemId()).Statistics.Updated;
                TokenCollections[collection.GetCollectionLabel()] = collection;
			}
		}

		public virtual string ReplaceRTETokens(RenderFieldArgs args, string text)
		{
			StringBuilder sb = new StringBuilder(text);
			var current = args.GetField().ID;
			if (!TrackTokens(args.Item, current, args.Item.Language, args.Item.Version.Number, text))
				return text;
			foreach (var location in TokenLocations[args.Item.ID.ToString() + current + args.Item.Language.Name + args.Item.Version.Number].Item2)
			{
				if (location.Item1 + location.Item2 > text.Length)
				{
					ResetTokenLocations(args.Item.ID, current, args.Item.Language, args.Item.Version.Number);
					return ReplaceRTETokens(args, text);
				}
				string token = sb.ToString(location.Item1, location.Item2);
				if (token.StartsWith(TokenPrefix) && token.EndsWith(TokenSuffix))
					sb.Replace(token, ParseTokenValueFromTokenIdentifier(token, args.Item), location.Item1, location.Item2);
				else
				{
					ResetTokenLocations(args.Item.ID, current, args.Item.Language, args.Item.Version.Number);
					return ReplaceRTETokens(args, text);
				}
			}
			return sb.ToString();
		}

		public virtual IEnumerable<IToken> ParseTokens(Field field)
		{
			return ParseTokenIdentifiers(field).Select(ParseITokenFromText);
		}

		public virtual IEnumerable<string> ParseTokenIdentifiers(Field field)
		{
			string text = field.Value;
			StringBuilder sb = new StringBuilder(text);
			var locations = ParseTokenLocations(field);
			List<string> ret = new List<string>();
			if (locations == null) return ret;
			foreach (var tokenProps in locations.Item2.Select(location => sb.ToString(location.Item1, location.Item2)))
			{
				if (!tokenProps.StartsWith(TokenPrefix) || !tokenProps.EndsWith(TokenSuffix))
				{
					ResetTokenLocations(field.Item.ID, field.ID, field.Language, field.Item.Version.Number);
					return ParseTokenIdentifiers(field);
				}
				ret.Add(tokenProps);
			}
			return ret;
		}

		public virtual Tuple<DateTime, List<Tuple<int, int>>> ParseTokenLocations(Field field)
		{
			var text = field.Value;
			return TrackTokens(field.Item, field.ID, field.Language, field.Item.Version.Number, text) ? TokenLocations[field.Item.ID.ToString() + field.ID + field.Language.Name + field.Item.Version.Number] : new Tuple<DateTime, List<Tuple<int, int>>>(DateTime.Now,new List<Tuple<int, int>>());
		}

		public virtual IToken ParseITokenFromText(string token)
		{
			var props = TokenProperties(token);
			return ParseITokenFromProps(props);
		}

		public virtual IToken ParseITokenFromProps(NameValueCollection props)
		{
			return GetToken(props["Category"], props["Token"]);
		}

		public virtual string ParseTokenValueFromTokenIdentifier(string token, Item item = null)
		{
			var props = TokenProperties(token);
			IToken t = ParseITokenFromProps(props);
			return t != null ? t.Value(props) : string.Empty;
		}

		public virtual string GetTokenIdentifier(NameValueCollection data)
		{
			return GetTokenIdentifier(data["Category"], data["Token"], data.AllKeys.Where(k => k != "Category" && k != "Token").ToDictionary(k => k, k => data[k]));
		}

		public virtual string GetTokenIdentifier(string category, string token, dynamic data)
		{
			return GetTokenIdentifier(category, token, data as IDictionary<string, object>);
		}

		public virtual string GetTokenIdentifier(string category, string token, IDictionary<string, object> fields)
		{
			var ret = HttpUtility.ParseQueryString("");
			ret["Category"] = category;
			ret["Token"] = token;
			if (fields == null)
				return string.Format("{0}{1}\" {5}>{2} > {3}{4}", TokenPrefix, ret, category, token, TokenSuffix,
					$"style='{TokenCss}'");
			foreach (string key in fields.Keys.Where(x=>x != "Category" && x != "Token"))
				ret.Add(key, fields[key].ToString());
			return string.Format("{0}{1}\" {5}>{2} > {3}{4}", TokenPrefix, ret, category, token, TokenSuffix,$"style='{TokenCss}'");
		}

		public virtual string GetTokenValue(string category, string token, NameValueCollection extraData)
		{
			if (TokenCollections.ContainsKey(category) && TokenCollections[category].IsCurrentContextValid() && IsCollectionValid(TokenCollections[category]))
				return TokenCollections[category][token].Value(extraData);
			return null;
		}

		public virtual IEnumerable<ContentSearchTokens> GetTokenOccurances(string category, string token)
		{
			return GetTokenOccurances(category, token, GetDatabase().Name);
		}
		public virtual IEnumerable<ContentSearchTokens> GetTokenOccurances(string category, string token, ID root)
		{
			return GetTokenOccurances(category, token, GetDatabase(), root);
		}

		public virtual IEnumerable<ContentSearchTokens> GetTokenOccurances(string category, string token, Database db)
		{
			return GetTokenOccurances(category, token, db.Name);
		}
		public virtual IEnumerable<ContentSearchTokens> GetTokenOccurances(string category, string token, Database db, ID root)
		{
			var item = db.GetItem(root);
			if (item == null)
				return GetTokenOccurances(category, token, db.Name);
			else
			{
				var tmp = GetTokenOccurances(category, token, db.Name).ToList();
				return tmp.Where(x => x.Path.StartsWith(item.Paths.FullPath));
			}
		}

		public virtual IEnumerable<ContentSearchTokens> GetTokenOccurances(string category, string token, string db)
		{
			var index = ContentSearchManager.GetIndex("sitecore_" + db + "_index")
				.CreateSearchContext(SearchSecurityOptions.DisableSecurityCheck);
			var query = index.GetQueryable<ContentSearchTokens>()
				.Where(t => t.Tokens.Contains(category + token));
			return query;
		}

		public virtual IEnumerable<string> GetTokenCollectionNames()
		{
			return GetTokenCollections().Select(x=>x.GetCollectionLabel());
		}

		public virtual IEnumerable<ITokenCollection<IToken>> GetTokenCollections()
		{
			return TokenCollections.Values.Where(c => c.IsCurrentContextValid() ).OrderBy(x=>x.GetCollectionLabel());
		}

		public virtual ITokenCollection<T> GetTokenCollection<T>(string collectionName)
			where T : IToken
		{

			if (!TokenCollections.ContainsKey(collectionName) || !IsCollectionValid(TokenCollections[collectionName]))
			{
				var collection = RefreshTokenCollection(collectionName);
				if (collection != null)
				{
					LoadTokenCollection(collection);
					collectionName = collection.GetCollectionLabel();
				}
				else if (TokenCollections.ContainsKey(collectionName))
					RemoveCollection(collectionName);


			}
			if (TokenCollections.ContainsKey(collectionName) && TokenCollections[collectionName].IsCurrentContextValid() && IsCollectionValid(TokenCollections[collectionName]))
				return TokenCollections[collectionName] as ITokenCollection<T>;
			return null;
		}

		public ITokenCollection<T> GetTokenCollection<T>(ID backingItemId) where T : IToken
		{
			var ret =  GetTokenCollections().FirstOrDefault(x => x.GetBackingItemId() == backingItemId) as ITokenCollection<T>;
			if (ret != null)
				return GetTokenCollection<T>(ret.GetCollectionLabel());
			var item = GetDatabase().GetItem(backingItemId);
			if (item == null) return null;
			var collection = GetCollectionFromItem(item);
			if (collection == null) return null;
			LoadTokenCollection(collection);
			return collection as ITokenCollection<T>;
		}

		public virtual IEnumerable<IToken> GetTokens(string category)
		{
			if (!TokenCollections.ContainsKey(category)) return null;
			var collection = TokenCollections[category];
			return collection?.GetTokens();
		}

		public virtual IToken GetToken(string category, string token)
		{
			var collection = GetTokenCollection<IToken>(category);
			IToken ret = collection?.GetToken(token);
			return ret;
		}

		public virtual ITokenCollection<IToken> RemoveCollection(string collectionLabel)
		{
			ITokenCollection<IToken> ret;
			TokenCollections.TryRemove(collectionLabel, out ret);
			return ret;
		}

		public virtual void ResetTokenLocations(ID itemId, ID fieldId, Language language, int versionNumber)
		{
			Tuple<DateTime, List<Tuple<int, int>>> ignored;
			string key = itemId.ToString() + fieldId + language.Name + versionNumber;
			if (TokenLocations.ContainsKey(key))
				TokenLocations.TryRemove(key, out ignored);
		}

		public virtual ITokenCollection<IToken> GetCollectionFromItem(Item item)
		{
			var args = new GetTokenCollectionTypeArgs()
			{
				CollectionItem = item
			};
			var pipeline = CorePipelineFactory.GetPipeline("getTokenCollection", string.Empty);
			pipeline.Run(args);
			return args.Collection;
		}

		public virtual NameValueCollection TokenProperties(string tokenIdentifier)
		{
			if (string.IsNullOrWhiteSpace(tokenIdentifier))
				return new NameValueCollection();
			if (!tokenIdentifier.StartsWith(TokenPrefix) || !tokenIdentifier.EndsWith(TokenSuffix))
				return HttpUtility.ParseQueryString(tokenIdentifier);
			int end = tokenIdentifier.IndexOf('"', TokenPrefix.Length);
			tokenIdentifier = HttpUtility.HtmlDecode(tokenIdentifier.Substring(TokenPrefix.Length,
				end - TokenPrefix.Length));
			return HttpUtility.ParseQueryString(tokenIdentifier);
		}

		public virtual bool IsInToken(Field field, int startIndex, int length)
		{
			var locations = ParseTokenLocations(field);
			if (locations == null)
				return false;
			foreach (var location in locations.Item2.TakeWhile(location => location.Item1 + location.Item2 >= startIndex))
			{
				if (startIndex < location.Item1 && startIndex + length > location.Item1)
					return true;
				if (startIndex + length > location.Item1 + location.Item2 && startIndex < location.Item1 + location.Item2)
					return true;
				if (startIndex > location.Item1 && startIndex + length < location.Item1 + location.Item2)
					return true;
			}
			return false;
		}

		public virtual Database GetDatabase()
		{
			if (Context.ContentDatabase != null)
				return Context.ContentDatabase;
			if (Context.Database != null)
				return Context.Database;
			var master = Factory.GetDatabases().FirstOrDefault(x => x.HasContentItem && x.Name=="master");
			if (master != null)
				return master;
			if (HttpContext.Current == null)
				return Context.Site != null ? Context.Site.Database : Factory.GetDatabases().FirstOrDefault(x => x.HasContentItem);
			var url = HttpContext.Current.Request.Url;
			var siteContext = Sitecore.Sites.SiteContextFactory.GetSiteContext(url.Host, url.PathAndQuery);
			if (siteContext != null)
			{
				return siteContext.Database;
			}
			return Context.Site != null ? Context.Site.Database : Factory.GetDatabases().FirstOrDefault(x=>x.HasContentItem);
		}

		/// <summary>
		/// finds the token locations and stores it in a cache
		/// </summary>
		/// <param name="item"></param>
		/// <param name="fieldId"></param>
		/// <param name="version"></param>
		/// <param name="text"></param>
		/// <param name="language"></param>
		/// <returns>if there are any tokens found</returns>
		private bool IdentifyTokenLocations(Item item, ID fieldId, Language language, int version, string text)
		{
			if (TokenPrefix == null || TokenSuffix == null)
				return false;
			List<Tuple<int, int>> locations = new List<Tuple<int, int>>();
			int startIndex = text.IndexOf(TokenPrefix, StringComparison.Ordinal);
			while (startIndex > -1)
			{
				var endIndex = text.IndexOf(TokenSuffix, startIndex, StringComparison.Ordinal);
				if (endIndex != -1)
					endIndex += TokenSuffix.Length;
				else
					break;
				locations.Insert(0, new Tuple<int, int>(startIndex, endIndex - startIndex));
				startIndex = text.IndexOf(TokenPrefix, endIndex, StringComparison.Ordinal);
			}
			var ret = new Tuple<DateTime, List<Tuple<int, int>>>(item.Statistics.Updated, locations);
			TokenLocations.AddOrUpdate(item.ID + fieldId.ToString() + language.Name + version, ret, (key, value) => ret);
			return locations.Any();
		}

		/// <summary>
		/// finds if the locations are cached and if not calls to have them identified
		/// </summary>
		/// <param name="item"></param>
		/// <param name="fieldId"></param>
		/// <param name="version"></param>
		/// <param name="text"></param>
		/// <param name="language"></param>
		/// <returns>if there are any tokens</returns>
		private bool TrackTokens(Item item, ID fieldId, Language language, int version, string text)
		{
			var key = item.ID + fieldId.ToString() + language.Name + version;
			if (TokenLocations.ContainsKey(key) && TokenLocations[key].Item1 == item.Statistics.Updated)
				return TokenLocations[key].Item2.Any();
			return IdentifyTokenLocations(item, fieldId, language, version, text) && TokenLocations[key].Item2.Any();
		}

		/// <summary>
		/// checks if the token collection is valid
		/// </summary>
		/// <param name="collection"></param>
		/// <returns>boolean for if the token is valid</returns>
		private bool IsCollectionValid(ITokenCollection<IToken> collection)
		{
			var item = GetDatabase().GetItem(collection.GetBackingItemId());
            if (item != null && item.Statistics.Updated <= TokenCacheUpdateTimes[collection.GetCollectionLabel()])
				return true;
			return false;
		}

		/// <summary>
		/// Refreshes the token collection with what's in sitecore
		/// </summary>
		/// <param name="category"></param>
		/// <returns>token collection</returns>
		private ITokenCollection<IToken> RefreshTokenCollection(string category)
		{
			Item tokenManagerItem = GetDatabase().GetItem(Constants.TokenManagerGuid);
			ITokenCollection<IToken> collection = TokenCollections.ContainsKey(category) ? TokenCollections[category]:null;
			if (tokenManagerItem != null)
			{
				Stack<Item> curItems = new Stack<Item>();
				curItems.Push(tokenManagerItem);
				while (curItems.Any())
				{
					Item cur = curItems.Pop();
					// this means that the collection exists, it's just out of date, so we need to update it.
					if (collection != null)
					{
						if (collection.GetBackingItemId() == cur.ID)
						{
							ITokenCollection<IToken> col = GetCollectionFromItem(cur);
							LoadTokenCollection(col);
							RemoveCollection(category);
							return col;
						}
					}
					else
					// this means that the token doesn't exist yet, lets create it.
					{
						ITokenCollection<IToken> col = GetCollectionFromItem(cur);
						if (col != null && col.GetCollectionLabel() == category)
						{
							LoadTokenCollection(col);
							return col;
						}
					}
					foreach (Item child in cur.Children)
						curItems.Push(child);
				}
			}
			return null;
		}
	}
}