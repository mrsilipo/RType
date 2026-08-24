namespace RType.Vehicle;

public readonly record struct DrivenWheelSet(bool FrontLeft, bool FrontRight, bool RearLeft, bool RearRight)
{
    public int Count
    {
        get
        {
            int count = 0;
            if (FrontLeft)
            {
                count++;
            }

            if (FrontRight)
            {
                count++;
            }

            if (RearLeft)
            {
                count++;
            }

            if (RearRight)
            {
                count++;
            }

            return count;
        }
    }

    public bool IsDriven(WheelCorner corner)
    {
        return corner switch
        {
            WheelCorner.FrontLeft => FrontLeft,
            WheelCorner.FrontRight => FrontRight,
            WheelCorner.RearLeft => RearLeft,
            WheelCorner.RearRight => RearRight,
            _ => false
        };
    }
}
