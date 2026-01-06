using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Move : MonoBehaviour
{
    public float moveForce = 40f;
    public float maxSpeed = 7f;
    public float jumpForce = 18f;

    public Grapple grapple;

    private Rigidbody2D rb;
    private float inputX;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        inputX = Input.GetAxisRaw("Horizontal");

        if (Input.GetKeyDown(KeyCode.Space) && IsGrounded() && !grapple.IsGrappling())
        {
            rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
        }
    }

    void FixedUpdate()
    {
        float control = grapple.IsGrappling() ? 0.4f : 1f;

        rb.AddForce(Vector2.right * inputX * moveForce * control);

        rb.linearVelocity = new Vector2(
            Mathf.Clamp(rb.linearVelocity.x, -maxSpeed, maxSpeed),
            rb.linearVelocity.y
        );
    }

    bool IsGrounded()
    {
        return Mathf.Abs(rb.linearVelocity.y) < 0.05f;
    }
}
