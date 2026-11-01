using System;
using System.Drawing;

namespace Shooting;

public class BaseSubPlane : BaseObject
{
	public double ShootDirection { get; set; }

	public BaseSubPlane(StageDataPackage StageData, string textureName, PointF Position)
		: base(StageData, textureName, Position, 0f, Math.PI / 2.0)
	{
		base.TransparentValueF = 0f;
		base.TransparentVelocity = 20f;
		base.AngleWithDirection = false;
		base.AngleDegree = 90.0;
	}

	public virtual void Refresh()
	{
		base.Time = 0;
	}

	public override void Ctrl()
	{
		base.Ctrl();
		_ = base.DestPoint;
		MoveToPoint(base.DestPoint);
	}

	public void MoveToPoint(PointF DP)
	{
		float num = (float)GetDistance(DP);
		if (num > 2f)
		{
			base.Velocity = num / 2.5f;
		}
		else if (num > 0f)
		{
			base.Velocity = 0.5f;
		}
		else
		{
			base.Velocity = 0f;
		}
		base.Direction = GetDirection(DP);
	}

	public override void Shoot()
	{
		BulletGuidance bulletGuidance = new BulletGuidance(base.StageData, "子机子弹", base.Position, 8f, -Math.PI / 2.0);
		bulletGuidance.TransparentValueF = 150f;
		bulletGuidance.Angle = 3.1415927410125732;
	}

	public override void Show()
	{
		if (base.Enabled)
		{
			base.Show();
		}
	}
}
