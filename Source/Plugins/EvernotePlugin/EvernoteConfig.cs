﻿using AlephNote.PluginInterface;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;
using AlephNote.PluginInterface.Objects;
using AlephNote.PluginInterface.Objects.AXML;
using AlephNote.PluginInterface.Util;

namespace AlephNote.Plugins.Evernote
{
	public class EvernoteConfig : IRemoteStorageConfiguration
	{
		private const string ENCRYPTION_KEY = @"uRCXcsCesQvZd3pUUWB5hUYZfGX8bztG"; // https://duckduckgo.com/?q=random+password+32+characters
		
		private const int ID_MAIL     = 6551;
		private const int ID_PASSWORD = 6552;
		private const int ID_SANDBOX  = 6553;

		public string Email    = string.Empty;
		public string Password = string.Empty;
		public bool UseSandbox = false;

		public XElement Serialize(AXMLSerializationSettings opt)
		{
			var data = new object[]
			{
				new XElement("Username", Email),
				new XElement("Password", Encrypt(Password, opt)),
				new XElement("UseSandbox", UseSandbox),
			};

			var r = new XElement("config", data);
			r.SetAttributeValue("plugin", EvernotePlugin.Name);
			r.SetAttributeValue("pluginversion", EvernotePlugin.Version.ToString());
			return r;
		}

		public void Deserialize(XElement input, AXMLSerializationSettings opt)
		{
			if (input.Name.LocalName != "config") throw new Exception("LocalName != 'config'");

			Email = XHelper.GetChildValue(input, "Username", Email);
			Password = Decrypt(XHelper.GetChildValue(input, "Password", string.Empty), opt);
			UseSandbox = XHelper.GetChildValue(input, "UseSandbox", UseSandbox);
		}

		public IEnumerable<DynamicSettingValue> ListProperties()
		{
			yield return DynamicSettingValue.CreateText(ID_MAIL, "Email Adress", Email);
			yield return DynamicSettingValue.CreatePassword(ID_PASSWORD, "Password", Password);
			yield return DynamicSettingValue.CreateCheckbox(ID_SANDBOX, "Use sandbox server", UseSandbox);
		}

		public void SetProperty(int id, string value)
		{
			if (id == ID_MAIL) Email = value;
			if (id == ID_PASSWORD) Password = value;
		}

		public void SetProperty(int id, bool value)
		{
			if (id == ID_SANDBOX) UseSandbox = value;
		}

		public void SetProperty(int id, int value)
		{
			throw new NotSupportedException();
		}

		public bool IsEqual(IRemoteStorageConfiguration iother)
		{
			var other = iother as EvernoteConfig;
			if (other == null) return false;

			if (this.Email      != other.Email) return false;
			if (this.Password   != other.Password) return false;
			if (this.UseSandbox != other.UseSandbox) return false;

			return true;
		}

		public IRemoteStorageConfiguration Clone()
		{
			return new EvernoteConfig
			{
				Email      = this.Email,
				Password   = this.Password,
				UseSandbox = this.UseSandbox,
			};
		}

		private string Encrypt(string data, AXMLSerializationSettings opt)
		{
			return ANEncryptionHelper.SimpleEncryptWithPassword(data, ENCRYPTION_KEY, opt);
		}

		private string Decrypt(string data, AXMLSerializationSettings opt)
		{
			return ANEncryptionHelper.SimpleDecryptWithPassword(data, ENCRYPTION_KEY, opt);
		}

		public string GetDisplayIdentifier()
		{
			return Email;
		}
	}
}
