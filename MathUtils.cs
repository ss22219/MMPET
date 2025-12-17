using System;

namespace MMPET;

public static class MathUtils
{
    /// <summary>
    /// 3D世界坐标转换为屏幕坐标 (UE4/UE5标准方法)
    /// </summary>
    public static (double X, double Y)? WorldToScreen(
        FVector worldPos, 
        FVector cameraPos, 
        FRotator cameraRot, 
        double screenWidth, 
        double screenHeight, 
        double fov = 90.0)
    {
        try
        {
            // UE4坐标系: X=前, Y=右, Z=上
            var deltaX = worldPos.X - cameraPos.X;
            var deltaY = worldPos.Y - cameraPos.Y;
            var deltaZ = worldPos.Z - cameraPos.Z;

            // UE4旋转: Pitch=俯仰, Yaw=偏航, Roll=翻滚
            var yaw = DegreesToRadians(cameraRot.Yaw);
            var pitch = DegreesToRadians(cameraRot.Pitch);
            var roll = DegreesToRadians(cameraRot.Roll);

            // 构建旋转矩阵 (ZYX顺序，符合UE4标准)
            var cosYaw = Math.Cos(yaw);
            var sinYaw = Math.Sin(yaw);
            var cosPitch = Math.Cos(pitch);
            var sinPitch = Math.Sin(pitch);
            var cosRoll = Math.Cos(roll);
            var sinRoll = Math.Sin(roll);

            // 应用旋转变换到摄像头本地坐标系
            // 先应用Yaw (Z轴旋转)
            var x1 = deltaX * cosYaw + deltaY * sinYaw;
            var y1 = -deltaX * sinYaw + deltaY * cosYaw;
            var z1 = deltaZ;

            // 再应用Pitch (Y轴旋转)
            var x2 = x1 * cosPitch - z1 * sinPitch;
            var y2 = y1;
            var z2 = x1 * sinPitch + z1 * cosPitch;

            // 最后应用Roll (X轴旋转)
            var x3 = x2;
            var y3 = y2 * cosRoll + z2 * sinRoll;
            var z3 = -y2 * sinRoll + z2 * cosRoll;

            // 检查是否在摄像头前方
            if (x3 <= 1.0) // 最小距离
                return null;

            // 透视投影
            var fovRad = DegreesToRadians(fov);
            var tanHalfFov = Math.Tan(fovRad / 2.0);
            
            // 计算NDC坐标 (-1 到 1)
            var ndcX = y3 / (x3 * tanHalfFov);
            var ndcY = z3 / (x3 * tanHalfFov);

            // 转换到屏幕坐标
            var screenX = (ndcX + 1.0) * screenWidth * 0.5;
            var screenY = (1.0 - ndcY) * screenHeight * 0.5;

            return (screenX, screenY);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 宽松的FOV算法（主要使用）
    /// </summary>
    public static (double X, double Y)? RelaxedFOVWorldToScreen(
        FVector worldPos, 
        FVector cameraPos, 
        FRotator cameraRot, 
        double screenWidth, 
        double screenHeight,
        double fovDegrees = 90.0)
    {
        try
        {
            // 计算相对位置
            var deltaX = worldPos.X - cameraPos.X;
            var deltaY = worldPos.Y - cameraPos.Y;
            var deltaZ = worldPos.Z - cameraPos.Z;

            // 简化的角度计算
            var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (distance < 0.1) return null;

            // 计算角度差
            var targetYaw = Math.Atan2(deltaY, deltaX) * 180.0 / Math.PI;
            var targetPitch = Math.Atan2(deltaZ, distance) * 180.0 / Math.PI;
            
            var yawDiff = NormalizeAngle((float)(targetYaw - cameraRot.Yaw));
            var pitchDiff = (float)(targetPitch - cameraRot.Pitch);

            // 使用FOV进行屏幕映射
            var halfFovDeg = fovDegrees / 2.0;
            
            // 将角度差映射到屏幕坐标
            var screenX = screenWidth * 0.5 + yawDiff / halfFovDeg * screenWidth * 0.5;
            var screenY = screenHeight * 0.5 - pitchDiff / halfFovDeg * screenHeight * 0.5;

            return (screenX, screenY);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 简化的直接坐标转换（备用算法）
    /// </summary>
    public static (double X, double Y)? DirectWorldToScreen(
        FVector worldPos, 
        FVector cameraPos, 
        FRotator cameraRot, 
        double screenWidth, 
        double screenHeight,
        float sensitivity = 1.0f)
    {
        try
        {
            // 计算相对位置
            var deltaX = worldPos.X - cameraPos.X;
            var deltaY = worldPos.Y - cameraPos.Y;
            var deltaZ = worldPos.Z - cameraPos.Z;
            
            // 计算距离
            var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (distance < 1.0) return null;
            
            // 计算水平角度
            var targetYaw = Math.Atan2(deltaY, deltaX) * 180.0 / Math.PI;
            var yawDiff = NormalizeAngle((float)(targetYaw - cameraRot.Yaw));
            
            // 计算垂直角度
            var targetPitch = Math.Atan2(deltaZ, distance) * 180.0 / Math.PI;
            var pitchDiff = (float)(targetPitch - cameraRot.Pitch);
            
            // 转换为屏幕坐标（使用更直接的映射）
            var screenX = screenWidth * 0.5 + yawDiff * sensitivity * 2.0;
            var screenY = screenHeight * 0.5 - pitchDiff * sensitivity * 2.0;
            
            // 更宽松的范围检查
            if (Math.Abs(yawDiff) > 90 || Math.Abs(pitchDiff) > 60)
                return null;
                
            return (screenX, screenY);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 计算两点之间的距离
    /// </summary>
    public static double Distance3D(FVector pos1, FVector pos2)
    {
        var dx = pos1.X - pos2.X;
        var dy = pos1.Y - pos2.Y;
        var dz = pos1.Z - pos2.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// 角度转弧度
    /// </summary>
    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    /// <summary>
    /// 标准化角度到 -180 到 180 范围
    /// </summary>
    public static float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }
}