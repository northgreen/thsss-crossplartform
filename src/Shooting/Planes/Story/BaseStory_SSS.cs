using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using SlimDX.Direct3D9;

namespace Shooting.Planes.Story;

internal class BaseStory_SSS : BaseStory
{
	protected List<Conversation> Conv = new List<Conversation>();

	protected CharacterL_Touhou CharL;

	protected CharacterL_Touhou CharL2;

	protected CharacterR_Touhou CharR;

	protected StoryBox SBox;

	protected CharacterName CharN;

	protected int LastTime;

	protected int NameTime;

	protected int MusicTime;

	public BaseStory_SSS()
	{
	}

	public BaseStory_SSS(StageDataPackage StageData)
		: base(StageData)
	{
		CharR = new CharacterR_Touhou(StageData, " ")
		{
			OriginalPosition = new PointF(base.BoundRect.Width - 60, 380f)
		};
		CharL = new CharacterL_Touhou(StageData, " ")
		{
			OriginalPosition = new PointF(70f, 380f)
		};
		CharL2 = new CharacterL_Touhou(StageData, " ")
		{
			OriginalPosition = new PointF(70f, 380f)
		};
		SBox = new StoryBox(StageData);
	}

	public override void Ctrl()
	{
		base.Ctrl();
		CharL.Ctrl();
		CharL2.Ctrl();
		CharR.Ctrl();
		SBox.Ctrl();
		foreach (BaseObject x in SlimDX.Compat.ListCompat.LegacyItems(StoryEffectList))
		{
			x.Ctrl();
			if (x.OutBoundary())
			{
				StoryEffectList.Remove(x);
			}
		}
		if (CharN != null)
		{
			CharN.Ctrl();
		}
		if (base.Time < Conv.Count)
		{
			CharR.Active = Conv[base.Time].CharRAcitve;
			CharL.Active = Conv[base.Time].CharLAcitve;
			CharL2.Active = Conv[base.Time].CharL2Acitve;
			if (Conv[base.Time].CharL != null)
			{
				CharL.TxtureObject = base.TextureObjectDictionary[Conv[base.Time].CharL];
			}
			if (Conv[base.Time].CharL2 != null)
			{
				CharL2.TxtureObject = base.TextureObjectDictionary[Conv[base.Time].CharL2];
			}
			if (Conv[base.Time].CharR != null)
			{
				CharR.TxtureObject = base.TextureObjectDictionary[Conv[base.Time].CharR];
			}
			SBox.Text = Conv[base.Time].Text;
		}
	}

	public override void Show()
	{
		if (CharR.Active)
		{
			if (CharL2.Active)
			{
				CharL2.Show();
				CharL.Show();
			}
			else
			{
				CharL.Show();
				CharL2.Show();
			}
			CharR.Show();
		}
		else
		{
			CharR.Show();
			if (CharL2.Active)
			{
				CharL.Show();
				CharL2.Show();
			}
			else
			{
				CharL2.Show();
				CharL.Show();
			}
		}
		SBox.Show();
		if (CharN != null)
		{
			CharN.Show();
		}
		if (StoryEffectList.Count <= 0)
		{
			return;
		}
		foreach (BaseObject x in SlimDX.Compat.ListCompat.LegacyItems(StoryEffectList))
		{if (!x.Active)
			{
				x.Show();
			}
				}
		base.SpriteMain.End();
		base.SpriteMain.Begin(SpriteFlags.AlphaBlend);
		base.StageData.DeviceMain.SetRenderState(RenderState.SourceBlend, 5);
		base.StageData.DeviceMain.SetRenderState(RenderState.DestinationBlend, 2);
		foreach (BaseObject x in SlimDX.Compat.ListCompat.LegacyItems(StoryEffectList))
		{if (x.Active)
			{
				x.Show();
			}
				}
		base.SpriteMain.End();
		base.SpriteMain.Begin(SpriteFlags.AlphaBlend);
	}

	protected virtual void LoadConversation(string fileName)
	{
		if (!File.Exists(fileName))
		{
			return;
		}
		char[] separator = new char[1] { ',' };
		string[] array = File.ReadAllLines(fileName, Encoding.GetEncoding(936));
		Conv.Clear();
		int num = 0;
		string[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			string[] array3 = array2[i].Split(separator);
			if (num == 0)
			{
				num++;
				NameTime = int.Parse(array3[0]);
				MusicTime = int.Parse(array3[1]);
			}
			else
			{
				Conversation item = new Conversation(array3[0], bool.Parse(array3[1]), bool.Parse(array3[2]), array3[3], array3[4]);
				Conv.Add(item);
			}
		}
	}

	protected virtual void LoadConversation2(string fileName)
	{
		if (!File.Exists(fileName))
		{
			return;
		}
		char[] separator = new char[1] { ',' };
		// Original dialogue CSVs are CP936; .NET's Encoding.Default is UTF-8.
		string[] array = File.ReadAllLines(fileName, Encoding.GetEncoding(936));
		Conv.Clear();
		int num = 0;
		string[] array2 = array;
		foreach (string text in array2)
		{
			string[] array3 = text.Split(separator);
			if (num == 0)
			{
				num++;
				NameTime = int.Parse(array3[0]);
				MusicTime = int.Parse(array3[1]);
			}
			else
			{
				Conversation item = new Conversation(array3[0], bool.Parse(array3[1]), array3[2], bool.Parse(array3[3]), array3[4], bool.Parse(array3[5]), array3[6]);
				Conv.Add(item);
			}
		}
	}
}
