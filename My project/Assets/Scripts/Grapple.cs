using UnityEngine;

public class Grapple : MonoBehaviour
{
    public LayerMask grappleLayer;
    public float maxGrappleDistance = 15f;
    public float pullForce = 25f;

    private Rigidbody2D rb;
    private RopeVerlet rope;

    private Vector2 grapplePoint;
    private bool grappling;

    void Awake()
    {
        rb = GetComponentInParent<Rigidbody2D>();
        rope = GetComponentInChildren<RopeVerlet>();
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
            TryGrapple();

        if (Input.GetMouseButtonUp(0))
            StopGrapple();
    }

    void FixedUpdate()
    {
        if (!grappling) return;

        Vector2 toHook = grapplePoint - rb.position;
        float distance = toHook.magnitude;

        if (distance > 1.5f)
            rb.AddForce(toHook.normalized * pullForce, ForceMode2D.Force);
    }

    void TryGrapple()
    {
        Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dir = (mouseWorld - rb.position).normalized;

        RaycastHit2D hit = Physics2D.Raycast(rb.position, dir, maxGrappleDistance, grappleLayer);

        if (!hit) return;

        grapplePoint = hit.point;
        grappling = true;

        rope.EnableRope(rb.position, grapplePoint);
    }

    void StopGrapple()
    {
        grappling = false;
        rope.DisableRope();
    }

    public bool IsGrappling() => grappling;
}
