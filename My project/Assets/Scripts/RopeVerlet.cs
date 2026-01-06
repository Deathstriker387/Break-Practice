using System.Collections.Generic;
using UnityEngine;

public class RopeVerlet : MonoBehaviour
{
    [Header("Rope")]
    public int segments = 40;
    public float segmentLength = 0.25f;

    [Header("Physics")]
    public Vector2 gravity = new Vector2(0, -1f);
    public float damping = 0.98f;
    public LayerMask collisionMask;
    public float collisionRadius = 0.1f;

    private LineRenderer lr;
    private List<Segment> rope = new();
    private bool active;

    private Vector2 start;
    private Vector2 end;

    struct Segment
    {
        public Vector2 pos, oldPos;
        public Segment(Vector2 p) { pos = oldPos = p; }
    }

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.positionCount = segments;
        lr.enabled = false;
    }

    void Update()
    {
        if (!active) return;

        for (int i = 0; i < rope.Count; i++)
            lr.SetPosition(i, rope[i].pos);
    }

    void FixedUpdate()
    {
        if (!active) return;

        Simulate();
        ApplyConstraints();
        HandleCollision();
    }

    void Simulate()
    {
        for (int i = 1; i < rope.Count; i++)
        {
            Vector2 velocity = (rope[i].pos - rope[i].oldPos) * damping;
            rope[i] = new Segment(rope[i].pos + velocity + gravity * Time.fixedDeltaTime);
        }
    }

    void ApplyConstraints()
    {
        rope[0] = new Segment(start);
        rope[^1] = new Segment(end);

        for (int i = 0; i < rope.Count - 1; i++)
        {
            Vector2 delta = rope[i + 1].pos - rope[i].pos;
            float dist = delta.magnitude;
            Vector2 change = delta.normalized * (dist - segmentLength);

            if (i != 0)
            {
                rope[i] = new Segment(rope[i].pos + change * 0.5f);
                rope[i + 1] = new Segment(rope[i + 1].pos - change * 0.5f);
            }
            else
            {
                rope[i + 1] = new Segment(rope[i + 1].pos - change);
            }
        }
    }

    void HandleCollision()
    {
        for (int i = 1; i < rope.Count; i++)
        {
            Collider2D col = Physics2D.OverlapCircle(rope[i].pos, collisionRadius, collisionMask);
            if (!col) continue;

            Vector2 p = col.ClosestPoint(rope[i].pos);
            rope[i] = new Segment(p);
        }
    }

    public void EnableRope(Vector2 playerPos, Vector2 anchor)
    {
        active = true;
        start = playerPos;
        end = anchor;

        rope.Clear();
        Vector2 dir = (anchor - playerPos).normalized;

        for (int i = 0; i < segments; i++)
            rope.Add(new Segment(playerPos + dir * segmentLength * i));

        lr.enabled = true;
    }

    public void DisableRope()
    {
        active = false;
        lr.enabled = false;
    }
}
