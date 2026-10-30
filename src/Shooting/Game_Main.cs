using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using SlimDX;
using SlimDX.Direct3D9;
using SlimDX.DirectInput;

namespace Shooting;

public class Game_Main
{
	public RenderForm_Main Form_Main;

	private GlobalDataPackage GDP = new GlobalDataPackage();

	/// <summary>
	/// Carries a start-state request until GameStatesDictionary is populated by the
	/// loading thread; issuing it earlier would index a half-built dictionary.
	/// </summary>
	private string _pendingStartState;

	/// <summary>Verification hook only: lets the launcher jump straight to a state.</summary>
	public void RequestState(string stateName) => _pendingStartState = stateName;

	private IGameState PresentState;

	private Dictionary<string, IGameState> GameStatesDictionary;

	private Thread LoadingThread;

	private Watch fpsTimer = new Watch();

	private KeyboardCapture KCapture;

	private JoystickCapture JCapture;

	private MapKey mapkey = new MapKey { Left = "Left", Right = "Right", Up = "Up", Down = "Down", Slow = "ShiftKey", Shoot = "Z", Bomb = "X" };

	private bool fullWindow = false;

	private bool useDirectInput = false;

	private bool verticalSync = false;

	private bool antiAlias = false;

	private bool deviceLost = false;

	private bool singleThreaded = false;

	private int windowSize = 0;

	private int presentInterval = 1;

	private bool fixedFPS = false;

	private int delayTolerance = 10;

	private int bgmVolume;

	private int seVolume;

	private string fontType;

	public bool initSuccess = false;

	private bool firstFrame = true;

	private bool OnPause = false;

	private bool InitFlag = false;

	public GlobalDataPackage GlobalData
	{
		get
		{
			return GDP;
		}
		set
		{
			GDP = value;
		}
	}

	public SlimDX.Direct3D9.Device DeviceMain
	{
		get
		{
			return GlobalData.DeviceMain;
		}
		set
		{
			GlobalData.DeviceMain = value;
		}
	}

	public Game_Main()
	{
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Expected O, but got Unknown
		//IL_03fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_04cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_04d6: Expected O, but got Unknown
		//IL_04e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ee: Expected O, but got Unknown
		Form_Main = new RenderForm_Main();
		((Form)Form_Main).FormClosing += new FormClosingEventHandler(Form_Main_FormClosing);
		((Form)Form_Main).Deactivate += Form_Main_Deactivate;
		((Form)Form_Main).Activated += Form_Main_Activated;
		MouseCtrl.DrawCursor(!fullWindow);
		Dpi.GetDpi();
		try
		{
			INI_RW iNI_RW = new INI_RW("Setting.INI");
			if (iNI_RW.ExistINIFile())
			{
				fullWindow = iNI_RW.IniReadValue("DirectX", "FullWindow") == "1";
				antiAlias = iNI_RW.IniReadValue("DirectX", "AntiAlias") == "1";
				verticalSync = iNI_RW.IniReadValue("DirectX", "VerticalSync") == "1";
				useDirectInput = iNI_RW.IniReadValue("DirectInput", "UseDirectInput") == "1";
				GlobalData.Clear = iNI_RW.IniReadValue("Mode", "Clear") == "yes";
				singleThreaded = iNI_RW.IniReadValue("Mode", "SingleThreaded") == "1";
				fixedFPS = iNI_RW.IniReadValue("Mode", "FixedFPS") == "1";
				delayTolerance = Convert.ToInt16(iNI_RW.IniReadValue("Mode", "DelayTolerance"));
				windowSize = Convert.ToInt16(iNI_RW.IniReadValue("Mode", "WindowSize"));
				presentInterval = Convert.ToInt16(iNI_RW.IniReadValue("Mode", "PresentInterval"));
				mapkey.Left = iNI_RW.IniReadValue("Key", "Left");
				mapkey.Right = iNI_RW.IniReadValue("Key", "Right");
				mapkey.Up = iNI_RW.IniReadValue("Key", "Up");
				mapkey.Down = iNI_RW.IniReadValue("Key", "Down");
				mapkey.Slow = iNI_RW.IniReadValue("Key", "Slow");
				mapkey.Shoot = iNI_RW.IniReadValue("Key", "Shoot");
				mapkey.Bomb = iNI_RW.IniReadValue("Key", "Bomb");
				MapJoyStick.Skip = Convert.ToInt16(iNI_RW.IniReadValue("JoyStick", "Skip"));
				MapJoyStick.Pause = Convert.ToInt16(iNI_RW.IniReadValue("JoyStick", "Pause"));
				MapJoyStick.Slow = Convert.ToInt16(iNI_RW.IniReadValue("JoyStick", "Slow"));
				MapJoyStick.Shoot = Convert.ToInt16(iNI_RW.IniReadValue("JoyStick", "Shoot"));
				MapJoyStick.Bomb = Convert.ToInt16(iNI_RW.IniReadValue("JoyStick", "Bomb"));
				MapJoyStick.C = Convert.ToInt16(iNI_RW.IniReadValue("JoyStick", "C"));
				bgmVolume = Convert.ToInt16(iNI_RW.IniReadValue("Volume", "BGMVolume"));
				seVolume = Convert.ToInt16(iNI_RW.IniReadValue("Volume", "SEVolume"));
				fontType = iNI_RW.IniReadValue("Game", "FontType");
			}
			iNI_RW = null;
		}
		catch (Exception ex)
		{
			MessageBox.Show("读取配置文件失败: " + ex.Message, "Setting Load Error");
			throw new InvalidDataException("Invalid Setting.INI; startup aborted before saving partial settings.", ex);
		}
		if (presentInterval < 1 || presentInterval > 3 || windowSize < 0 || windowSize > 2)
			throw new InvalidDataException("Setting.INI requires PresentInterval=1..3 and WindowSize=0..2.");
        Form_Main.ConfigurePresentation(fullWindow, verticalSync, presentInterval, gameOwnsPacing: true);
		if (windowSize == 1)
		{
			((Form)Form_Main).ClientSize = new Size(800, 600);
		}
		else if (windowSize == 2)
		{
			((Form)Form_Main).ClientSize = new Size(1024, 768);
		}
		if (!Direct3DInit())
		{
			Application.ExitThread();
			return;
		}
		initSuccess = true;
		if (useDirectInput)
		{
			KCapture = new KeyboardCapture(((Control)Form_Main).Handle);
			JCapture = new JoystickCapture(((Control)Form_Main).Handle);
		}
		else
		{
			((Control)Form_Main).KeyDown += new KeyEventHandler(Form_Main_KeyDown);
			((Control)Form_Main).KeyUp += new KeyEventHandler(Form_Main_KeyUp);
		}
		PresentState = new GameState_SplashScreen(GlobalData);
		PresentState.Init();
		PresentState.BGM_ON();
		GlobalData.LoadResources1();
		LoadingThread = new Thread(AddState);
		LoadingThread.Start();
	}

	public bool Direct3DInit()
	{
		//IL_01ff: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			Direct3D direct3D = new Direct3D();
			AdapterInformation defaultAdapter = direct3D.Adapters.DefaultAdapter;
			SlimDX.Direct3D9.Capabilities deviceCaps = direct3D.GetDeviceCaps(defaultAdapter.Adapter, SlimDX.Direct3D9.DeviceType.Hardware);
			CreateFlags createFlags = (((deviceCaps.DeviceCaps & DeviceCaps.HWTransformAndLight) != DeviceCaps.HWTransformAndLight) ? CreateFlags.SoftwareVertexProcessing : CreateFlags.HardwareVertexProcessing);
			GlobalData.presentParams = new PresentParameters
			{
				BackBufferWidth = 640,
				BackBufferHeight = 480,
				BackBufferFormat = defaultAdapter.CurrentDisplayMode.Format,
				DeviceWindowHandle = ((Control)Form_Main).Handle,
				Windowed = !fullWindow,
				PresentFlags = PresentFlags.DiscardDepthStencil,
				SwapEffect = SwapEffect.Discard
			};
			if (presentInterval == 1)
			{
				if (verticalSync)
				{
					GlobalData.presentParams.PresentationInterval = PresentInterval.Default;
				}
				else
				{
					GlobalData.presentParams.PresentationInterval = PresentInterval.Immediate;
				}
			}
			else if (fullWindow && verticalSync)
			{
				if (presentInterval == 2)
				{
					GlobalData.presentParams.PresentationInterval = PresentInterval.Two;
				}
				else if (presentInterval == 3)
				{
					GlobalData.presentParams.PresentationInterval = PresentInterval.Three;
				}
			}
			else
			{
				GlobalData.presentParams.PresentationInterval = PresentInterval.Immediate;
			}
			DeviceMain = new SlimDX.Direct3D9.Device(direct3D, defaultAdapter.Adapter, SlimDX.Direct3D9.DeviceType.Hardware, ((Control)Form_Main).Handle, createFlags, GlobalData.presentParams);
			// Match the original device's effect on game arithmetic without changing the host FPU or renderer.
			SlimDX.Compat.LegacyFloat.InitializeGameplayThread();
			GlobalData.SpriteMain = new MySprite(DeviceMain);
			return true;
		}
		catch (Exception initError)
		{
			Console.Error.WriteLine("[Direct3D Init] " + initError);
			MessageBox.Show("DirectX初始化失败", "DirectX Initial Error");
			return false;
		}
	}

	public void AddState()
	{
		Watch watch = new Watch();
		watch.Start();
		GlobalData.BGMVolume = bgmVolume;
		GlobalData.SEVolume = seVolume;
		GlobalData.FontType = fontType;
		GlobalData.LoadResources();
		GameStatesDictionary = new Dictionary<string, IGameState>
		{
			{ "SplashScreen", PresentState },
			{
				"Stage_test",
				new GameState_testStage(GlobalData)
			},
			{
				"St1",
				new GameState_SSS01(GlobalData)
			},
			{
				"St2",
				new GameState_SSS02(GlobalData)
			},
			{
				"St3",
				new GameState_SSS03(GlobalData)
			},
			{
				"St4",
				new GameState_SSS04(GlobalData)
			},
			{
				"St5",
				new GameState_SSS05(GlobalData)
			},
			{
				"St6",
				new GameState_SSS06(GlobalData)
			},
			{
				"StEx",
				new GameState_SSSEx(GlobalData)
			},
			{
				"Bs1",
				new GameState_Boss01(GlobalData)
			},
			{
				"Bs2",
				new GameState_Boss02(GlobalData)
			},
			{
				"Bs3",
				new GameState_Boss03(GlobalData)
			},
			{
				"Bs4",
				new GameState_Boss04(GlobalData)
			},
			{
				"Bs5",
				new GameState_Boss05(GlobalData)
			},
			{
				"Bs6",
				new GameState_Boss06(GlobalData)
			},
			{
				"All",
				new GameState_ED(GlobalData)
			},
			{
				"PauseMenu",
				new GameState_PauseMenu(GlobalData)
			},
			{
				"MainMenu",
				new GameState_MainMenu(GlobalData)
			},
			{
				"GameOverMenu",
				new GameState_GameOverMenu(GlobalData)
			},
			{
				"GameSetMenu",
				new GameState_GameSetMenu(GlobalData)
			},
			{
				"FinalReplaySaveMenu",
				new GameState_ReplaySaveMenu(GlobalData)
			},
			{
				"GAME_OVER",
				new GameState_SplashScreen2(GlobalData)
				{
					TxtureObject = GlobalData.TextureObjectDictionary["GAME_OVER"]
				}
			},
			{
				"此功能尚未开通",
				new GameState_SplashScreen2(GlobalData)
				{
					TxtureObject = GlobalData.TextureObjectDictionary["此功能尚未开通"]
				}
			},
			{
				"MusicLoading",
				new GameState_MusicLoading(GlobalData)
			}
		};
		GlobalData.Rep = new Replay();
		if (!singleThreaded)
		{
			watch.SleepTo(3000f);
		}
		if (antiAlias)
		{
			DeviceMain.SetRenderState(RenderState.MultisampleAntialias, value: true);
		}
		GlobalData.LastState = PresentState;
		InitFlag = true;
	}

	private void BeginFrameTiming(Action start, Action increaseOneFrame)
	{
		if (firstFrame)
		{
			start();
			firstFrame = false;
		}
		else if (!fixedFPS || PresentState is GameState_SplashScreen)
		{
			start();
		}
		else
		{
			for (int i = 0; i < presentInterval; i++)
			{
				increaseOneFrame();
			}
		}
	}

	public void MainProcess()
	{
		BeginFrameTiming(fpsTimer.Start, fpsTimer.IncreaseOneFrame);
		if (InitFlag && GlobalData.LoadingList.Count == 0)
		{
			GlobalData.LoadModelDictionary();
			InitFlag = false;
			PresentState.StateSwitchData = new StateSwitchDataPackage
			{
				NextState = "MainMenu",
				NeedInit = true,
				SDPswitch = new StageDataPackage(GlobalData)
			};
		}
		if (_pendingStartState != null && PresentState != null && PresentState.StateSwitchData == null
			&& GameStatesDictionary != null && GameStatesDictionary.ContainsKey(_pendingStartState))
		{
			var requested = _pendingStartState;
			_pendingStartState = null;
			PresentState.StateSwitchData = new StateSwitchDataPackage
			{
				NextState = requested,
				NeedInit = true,
				SDPswitch = new StageDataPackage(GlobalData),
			};
		}

		if (PresentState.StateSwitchData != null)
		{
			StateSwitchDataPackage stateSwitchData = PresentState.StateSwitchData;
			if (stateSwitchData.NextState == "GameExit")
			{
				((Form)Form_Main).Close();
				return;
			}
			if (stateSwitchData.NextState != null)
			{
				// A start-state request can arrive before the loading thread has
				// registered that state; skip it until the entry exists.
				if (!GameStatesDictionary.ContainsKey(stateSwitchData.NextState))
				{
					PresentState.StateSwitchData = null;
					return;
				}
				if (stateSwitchData.NextState == "MainMenu" && GlobalData.Rep != null)
				{
					GlobalData.Rep.CloseRpy();
				}
				GlobalData.BGM_Pause();
				PresentState.StateSwitchData = null;
				PresentState = GameStatesDictionary[stateSwitchData.NextState];
				if (stateSwitchData.SDPswitch != null)
				{
					PresentState.StageData = stateSwitchData.SDPswitch;
				}
				if (stateSwitchData.NeedInit)
				{
					PresentState.Init();
					PresentState.BGM_ON();
				}
				else
				{
					GlobalData.BGM_Resume();
				}
			}
		}
		if (useDirectInput)
		{
			KeyClass KClass = new KeyClass();
			KeyClass KClass2 = new KeyClass();
			try
			{
				KCapture.UpdateInput(ref KClass);
				KClass = LegacyKeyMap.Capture(mapkey, KCapture.currentKeyboardState);
			}
			catch
			{
			}
			try
			{
				JCapture.UpdateInput(ref KClass2);
			}
			catch
			{
			}
			KeyClass.KeyMix(ref GlobalData.KClass, KClass, KClass2);
			if (KCapture.currentKeyboardState != null)
			{
				if (KCapture.currentKeyboardState.IsPressed(Key.LeftAlt) && KCapture.currentKeyboardState.IsPressed(Key.F4))
				{
					((Form)Form_Main).Close();
					return;
				}
				if (KCapture.currentKeyboardState.IsPressed(Key.F8) && (KCapture.lastKeyboardState == null || KCapture.lastKeyboardState.IsReleased(Key.F8)))
				{
					WindowSwitch();
				}
				if (KCapture.currentKeyboardState.IsPressed(Key.P) && (KCapture.lastKeyboardState == null || KCapture.lastKeyboardState.IsReleased(Key.P)))
				{
					ScreenShot();
				}
			}
		}
		if (deviceLost)
		{
			Thread.Sleep(15);
			Result result = DeviceMain.TestCooperativeLevel();
			if (result == SlimDX.Direct3D9.ResultCode.DeviceNotReset)
			{
				GlobalData.OnResetDevice();
				deviceLost = false;
				firstFrame = true;
			}
			return;
		}
		try
		{
			if (OnPause && PresentState is BaseStage)
			{
				GlobalData.KClass.Key_ESC = true;
			}
			PresentState.UpdateData();
			for (int i = 1; i < presentInterval; i++)
			{
				if (PresentState.StateSwitchData != null)
				{
					if (PresentState.StateSwitchData.NextState == null)
					{
						PresentState.UpdateData();
					}
				}
				else
				{
					PresentState.UpdateData();
				}
			}
			DeviceMain.Clear(ClearFlags.ZBuffer | ClearFlags.Target, Color.Black, 1f, 0);
			DeviceMain.BeginScene();
			PresentState.Render();
			DeviceMain.EndScene();
			DeviceMain.Present(Present.None);
			if (GlobalData.BGM_Player.OnPause)
			{
				PresentState.BGM_Resume();
			}
		}
		catch (Direct3D9Exception ex)
		{
			if (ex.ResultCode == SlimDX.Direct3D9.ResultCode.DeviceLost)
			{
				GlobalData.OnLostDevice();
				deviceLost = true;
			}
			else
			{
				ErrorLog.SaveErrorLog(ex.Message, ex.ResultCode.Description);
				MessageBox.Show(ex.Message);
				((Form)Form_Main).Close();
			}
		}
		FinishFrameTiming(fpsTimer.GetDuration, Thread.Sleep, fpsTimer.IncreaseOneFrame);
	}

	private void FinishFrameTiming(Func<float> getDuration, Action<int> sleep, Action increaseOneFrame)
	{
		if (verticalSync && (presentInterval == 1 || fullWindow))
		{
			return;
		}
		float num = 16f * (float)presentInterval - getDuration();
		// This accumulated deadline must own pacing, including with a live window.
		if (num > 0f)
		{
			sleep((int)num);
		}
		while (16.62f * (float)presentInterval - getDuration() > 0f)
		{
		}
		if (fixedFPS && num < -16.66667f * (float)delayTolerance)
		{
			for (int i = 0; (float)i < (0f - num) / 16.66667f - (float)delayTolerance; i++)
			{
				increaseOneFrame();
			}
		}
	}

	private void ToggleFullScreen()
	{
		fullWindow = !fullWindow;
		GlobalData.presentParams.Windowed = !fullWindow;
		Form_Main.ConfigurePresentation(fullWindow, verticalSync, presentInterval, gameOwnsPacing: true);
		if (fullWindow && verticalSync)
		{
			if (presentInterval == 2)
			{
				GlobalData.presentParams.PresentationInterval = PresentInterval.Two;
			}
			else if (presentInterval == 3)
			{
				GlobalData.presentParams.PresentationInterval = PresentInterval.Three;
			}
		}
		else if (presentInterval == 2 || presentInterval == 3)
		{
			GlobalData.presentParams.PresentationInterval = PresentInterval.Immediate;
		}
		GlobalData.OnLostDevice();
		GlobalData.OnResetDevice();
		MouseCtrl.DrawCursor(!fullWindow);
	}

	private void WindowSwitch()
	{
		if (fullWindow)
		{
			ToggleFullScreen();
			((Form)Form_Main).ClientSize = new Size(640, 480);
			return;
		}
		switch (windowSize)
		{
		case 0:
			windowSize++;
			((Form)Form_Main).ClientSize = new Size(800, 600);
			break;
		case 1:
			windowSize++;
			((Form)Form_Main).ClientSize = new Size(1024, 768);
			break;
		case 2:
			windowSize = 0;
			ToggleFullScreen();
			break;
		}
	}

	private void Form_Main_Deactivate(object sender, EventArgs e)
	{
		if (initSuccess)
		{
			GlobalData.KClass = new KeyClass();
			OnPause = true;
		}
	}

	private void Form_Main_Activated(object sender, EventArgs e)
	{
		if (initSuccess)
		{
			OnPause = false;
			GlobalData.KClass.Key_ESC = false;
		}
	}

	public void RefreshInputDevices()
	{
		if (!useDirectInput) return;
		JCapture?.Dispose();
		JCapture = new JoystickCapture(((Control)Form_Main).Handle);
	}

	private void Form_Main_FormClosing(object sender, FormClosingEventArgs e)
	{
		try
		{
			if (LoadingThread != null && LoadingThread.IsAlive)
				SlimDX.Compat.ThreadCompat.Stop(LoadingThread);
		}
		catch (TimeoutException ex)
		{
			e.Cancel = true;
			MessageBox.Show(ex.Message, "Shutdown blocked: loading thread is still running");
			return;
		}
		KCapture?.Dispose();
		JCapture?.Dispose();
		GlobalData.PData?.SaveData();
		GlobalData.Rep?.CloseRpy();
		GlobalData.Dispose();
		if (GameStatesDictionary != null)
		{
			foreach (IGameState value in GameStatesDictionary.Values)
			{
				value.Dispose();
			}
		}
		GlobalData.DeviceMain?.Dispose();
		try
		{
			INI_RW iNI_RW = new INI_RW("Setting.INI");
			if (iNI_RW.ExistINIFile())
			{
				iNI_RW.IniWriteValue("Volume", "BGMVolume", GlobalData.BGMVolume.ToString());
				iNI_RW.IniWriteValue("Volume", "SEVolume", GlobalData.SEVolume.ToString());
			}
			iNI_RW = null;
		}
		catch
		{
		}
	}

	private void Form_Main_KeyDown(object sender, KeyEventArgs e)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_0166: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e5: Unknown result type (might be due to invalid IL or missing references)
		GlobalData.KClass.ArrowLeft = LegacyKeyMap.Name(e.KeyCode) == mapkey.Left || GlobalData.KClass.ArrowLeft;
		GlobalData.KClass.ArrowRight = LegacyKeyMap.Name(e.KeyCode) == mapkey.Right || GlobalData.KClass.ArrowRight;
		GlobalData.KClass.ArrowUp = LegacyKeyMap.Name(e.KeyCode) == mapkey.Up || GlobalData.KClass.ArrowUp;
		GlobalData.KClass.ArrowDown = LegacyKeyMap.Name(e.KeyCode) == mapkey.Down || GlobalData.KClass.ArrowDown;
		GlobalData.KClass.Key_Shift = LegacyKeyMap.Name(e.KeyCode) == mapkey.Slow || GlobalData.KClass.Key_Shift;
		GlobalData.KClass.Key_Z = LegacyKeyMap.Name(e.KeyCode) == mapkey.Shoot || GlobalData.KClass.Key_Z;
		GlobalData.KClass.Key_X = LegacyKeyMap.Name(e.KeyCode) == mapkey.Bomb || GlobalData.KClass.Key_X;
		switch (LegacyKeyMap.Name(e.KeyCode))
		{
		case "R":
			GlobalData.KClass.Key_C = true;
			break;
		case "ControlKey":
			GlobalData.KClass.Key_Ctrl = true;
			GlobalData.KClass.Key_plus = true;
			break;
		case "ShiftKey":
			GlobalData.KClass.Key_minus = true;
			break;
		case "Return":
			GlobalData.KClass.Key_Z = true;
			break;
		case "Escape":
			GlobalData.KClass.Key_ESC = true;
			break;
		case "Oemplus":
			GlobalData.KClass.Key_plus = true;
			break;
		case "OemMinus":
			GlobalData.KClass.Key_minus = true;
			break;
		case "F8":
			WindowSwitch();
			break;
		case "P":
			ScreenShot();
			break;
		}
	}

	private void Form_Main_KeyUp(object sender, KeyEventArgs e)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_0166: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e5: Unknown result type (might be due to invalid IL or missing references)
		GlobalData.KClass.ArrowLeft = !(LegacyKeyMap.Name(e.KeyCode) == mapkey.Left) && GlobalData.KClass.ArrowLeft;
		GlobalData.KClass.ArrowRight = !(LegacyKeyMap.Name(e.KeyCode) == mapkey.Right) && GlobalData.KClass.ArrowRight;
		GlobalData.KClass.ArrowUp = !(LegacyKeyMap.Name(e.KeyCode) == mapkey.Up) && GlobalData.KClass.ArrowUp;
		GlobalData.KClass.ArrowDown = !(LegacyKeyMap.Name(e.KeyCode) == mapkey.Down) && GlobalData.KClass.ArrowDown;
		GlobalData.KClass.Key_Shift = !(LegacyKeyMap.Name(e.KeyCode) == mapkey.Slow) && GlobalData.KClass.Key_Shift;
		GlobalData.KClass.Key_Z = !(LegacyKeyMap.Name(e.KeyCode) == mapkey.Shoot) && GlobalData.KClass.Key_Z;
		GlobalData.KClass.Key_X = !(LegacyKeyMap.Name(e.KeyCode) == mapkey.Bomb) && GlobalData.KClass.Key_X;
		switch (LegacyKeyMap.Name(e.KeyCode))
		{
		case "R":
			GlobalData.KClass.Key_C = false;
			break;
		case "ControlKey":
			GlobalData.KClass.Key_Ctrl = false;
			GlobalData.KClass.Key_plus = false;
			break;
		case "ShiftKey":
			GlobalData.KClass.Key_minus = false;
			break;
		case "Return":
			GlobalData.KClass.Key_Z = false;
			break;
		case "Escape":
			GlobalData.KClass.Key_ESC = false;
			break;
		case "Oemplus":
			GlobalData.KClass.Key_plus = false;
			break;
		case "OemMinus":
			GlobalData.KClass.Key_minus = false;
			break;
		}
	}

	private bool screenshotRequested;

	private void ScreenShot() => screenshotRequested = true;

	internal void CapturePendingScreenshot()
	{
		if (!screenshotRequested) return;
		screenshotRequested = false;
		using Surface backBuffer = DeviceMain.GetBackBuffer(0, 0);
		int i;
		for (i = 0; File.Exists($"ScreenShot/screen{i:0000}.jpg"); i++)
		{
		}
		Surface.ToFile(backBuffer, $"ScreenShot/screen{i:0000}.jpg", ImageFileFormat.Jpg);
		if (GlobalData != null && GlobalData.SoundDictionary != null && GlobalData.SoundDictionary.ContainsKey("se_shutter.wav"))
		{
			GlobalData.SoundDictionary["se_shutter.wav"].Play();
		}
	}
}
