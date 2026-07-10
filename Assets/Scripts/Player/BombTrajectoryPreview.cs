using UnityEngine;

public class BombTrajectoryPreview : MonoBehaviour
{
    [Header("Visuals")]
    [SerializeField] private Color trajectoryColor = Color.red;
    [SerializeField] private float lineWidth = 0.05f;
    [SerializeField] private float landingRingRadius = 0.4f;

    [Header("Simulation")]
    [SerializeField] private int resolution = 30;
    [SerializeField] private float simulationTime = 3f;
    [SerializeField] private LayerMask groundMask = ~0;

    private LineRenderer trajectoryLine;
    private LineRenderer landingRing;

    private void Awake()
    {
        trajectoryLine = CreateLineRenderer("TrajectoryLine", false);
        landingRing = CreateLineRenderer("LandingRing", true);
        Hide();
    }

    private LineRenderer CreateLineRenderer(string childName, bool loop)
    {
        GameObject child = new GameObject(childName);
        child.transform.SetParent(transform, false);

        LineRenderer line = child.AddComponent<LineRenderer>();
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = trajectoryColor;
        line.endColor = trajectoryColor;
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;
        line.loop = loop;
        line.useWorldSpace = true;
        line.enabled = false;

        return line;
    }

    public void Show(Vector3 origin, Vector3 velocity)
    {
        Vector3 gravity = Physics.gravity;
        Vector3 position = origin;
        Vector3 currentVelocity = velocity;
        float dt = simulationTime / resolution;

        Vector3[] points = new Vector3[resolution + 1];
        points[0] = position;

        Vector3 landingPoint = position;
        bool hitGround = false;

        for (int i = 1; i <= resolution; i++)
        {
            Vector3 previousPosition = position;
            currentVelocity += gravity * dt;
            position += currentVelocity * dt;

            if (!hitGround && Physics.Raycast(previousPosition, position - previousPosition, out RaycastHit hit, Vector3.Distance(previousPosition, position), groundMask))
            {
                position = hit.point;
                landingPoint = hit.point;
                hitGround = true;
            }

            points[i] = position;

            if (hitGround)
            {
                for (int j = i + 1; j <= resolution; j++)
                {
                    points[j] = position;
                }

                break;
            }
        }

        if (!hitGround)
        {
            landingPoint = position;
        }

        trajectoryLine.positionCount = points.Length;
        trajectoryLine.SetPositions(points);
        trajectoryLine.enabled = true;

        DrawLandingRing(landingPoint);
        landingRing.enabled = true;
    }

    private void DrawLandingRing(Vector3 center)
    {
        const int segments = 20;
        Vector3[] ringPoints = new Vector3[segments];

        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            ringPoints[i] = center + new Vector3(Mathf.Cos(angle), 0.02f, Mathf.Sin(angle)) * landingRingRadius;
        }

        landingRing.positionCount = segments;
        landingRing.SetPositions(ringPoints);
    }

    public void Hide()
    {
        trajectoryLine.enabled = false;
        landingRing.enabled = false;
    }
}
