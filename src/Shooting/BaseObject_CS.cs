using System;
using System.Collections.Generic;
using System.Drawing;

namespace Shooting;

public class BaseObject_CS : BaseObject
{
	public int type;

	public virtual int Type
	{
		get
		{
			return type;
		}
		set
		{
			type = value;
		}
	}

	public bool Ghosting
	{
		get
		{
			return base.GhostingCount > 0;
		}
		set
		{
			if (value)
			{
				base.GhostingCount = 20;
				base.GhostingColor = Color.White;
			}
			else
			{
				base.GhostingCount = 0;
			}
		}
	}

	public bool OutBound
	{
		get
		{
			return base.OutsideRegion < 80;
		}
		set
		{
			if (!value)
			{
				base.OutsideRegion = 200;
			}
		}
	}

	public bool BeginningEffect { get; set; }

	public bool EndingEffect { get; set; }

	public PointF CS_Position
	{
		get
		{
			return new PointF(base.OriginalPosition.X + 320f - 192f, base.OriginalPosition.Y + 240f - 224f);
		}
		set
		{
			base.OriginalPosition = new PointF(value.X - 320f + 192f, value.Y - 240f + 224f);
		}
	}

	public int ID { get; set; }

	public int LayerID { get; set; }

	public float AccelerateCS { get; set; }

	public double AccDirection { get; set; }

	public double RanAngle { get; set; }

	public float RanVelocity { get; set; }

	public double RanDirection { get; set; }

	public float RanAccelerate { get; set; }

	public double RanAccDirection { get; set; }

	public bool Cover { get; set; }

	public bool Rebound { get; set; }

	public bool Force { get; set; }

	public List<EventGroup> EventGroupList { get; set; }

	public List<Execution> EventsExecutionList { get; set; }

	public BaseObject_CS()
	{
	}

	public BaseObject_CS(StageDataPackage StageData)
		: base(StageData)
	{
	}

	public BaseObject_CS(StageDataPackage StageData, string textureName, PointF Position, float Velocity, double Direction)
		: base(StageData, textureName, Position, Velocity, Direction)
	{
	}

	public override void Ctrl()
	{
#if THSSS_MATH_OFF
		double num = AccDirection / 180.0 * Math.PI;
		base.Ax = AccelerateCS * (float)Math.Cos(num);
		base.Ay = AccelerateCS * (float)Math.Sin(num);
#else
		// Separate captures retain both script reads; active-stage ownership guards their observation window.
		PointF acceleration = SlimDX.Compat.LegacyFloat.ScriptAcceleration(AccDirection, AccelerateCS, AccelerateCS);
		base.Ax = acceleration.X;
		base.Ay = acceleration.Y;
#endif
		EventCtrl();
		base.Ctrl();
	}

	public virtual void EventCtrl()
	{
		if (EventGroupList != null)
		{
			foreach (EventGroup a in SlimDX.Compat.ListCompat.LegacyItems(EventGroupList)){
				a.Update(this);
			};
		}
		if (EventsExecutionList != null)
		{
			foreach (Execution a in SlimDX.Compat.ListCompat.LegacyItems(EventsExecutionList)){
				a.Update(this);
			};
		}
	}

	public override object Clone()
	{
		BaseObject_CS b = (BaseObject_CS)base.Clone();
		b.EventsExecutionList = new List<Execution>();
		b.EventGroupList = new List<EventGroup>();
		foreach (EventGroup x in SlimDX.Compat.ListCompat.LegacyItems(EventGroupList)){
			b.EventGroupList.Add((EventGroup)x.Clone());
		};
		return b;
	}
}
