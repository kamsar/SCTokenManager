﻿using System;
using System.Collections.Generic;
using System.Linq;
using Sitecore;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Data.Managers;
using Sitecore.StringExtensions;

namespace TokenManager.Handlers.ContentTree
{
	public class ContentTreeNode
	{
		public string Icon = "";
		public string DisplayName;
		public string Id;
		public bool Open;
		public List<ContentTreeNode> Nodes;

		public ContentTreeNode()
		{
		}

		public ContentTreeNode(Item item, bool open = true)
		{
			Open = open;
			SetIcon(item);
			DisplayName = item.DisplayName;
			Id = item.ID.ToString();
			if (Open)
				Nodes = item.Children.Select(c => new ContentTreeNode(c)).ToList();
		}

		public void SetIcon(Item item)
		{
			if (item != null)
			{
				Icon = GetSrc(ThemeManager.GetIconImage(item, 32, 32, "", ""));
			}
			//if (!string.IsNullOrWhiteSpace(Icon))
			//{
			//	string[] parts = Icon.Split('/');
			//	Icon = string.Join("/", parts.Skip(parts.Length - 3));
			//}
		}
		private string GetSrc(string imgTag)
		{
			int i1 = imgTag.IndexOf("src=\"", StringComparison.Ordinal) + 5;
			int i2 = imgTag.IndexOf("\"", i1, StringComparison.Ordinal);
			return imgTag.Substring(i1, i2 - i1);
		}
	}
}
