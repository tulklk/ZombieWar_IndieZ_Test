public class ZombieRunner : ZombieAI
{
    protected override float CalculateChaseSpeed(float elapsedChaseTime)
    {
        return runSpeed;
    }
}
