namespace RetroRacer.Vehicle;

public interface IVehicleSimulator
{
    VehicleState State { get; }

    void Update(VehicleInput input, float dt);
}
