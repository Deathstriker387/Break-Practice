using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float speed = 5.0f;
    public float jumpForce = 10.0f;

    private Rigidbody2D rb;
    private float horizontalInput;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");

        if (Input.GetKeyDown(KeyCode.Space) && Mathf.Abs(rb.linearVelocity.y) < 0.01f)
        {
            Jump();
        }
    }

    private void FixedUpdate()
    {

        rb.linearVelocity = new Vector2(horizontalInput * speed, rb.linearVelocity.y);
    }

    void Jump()
    {
        rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
    }
}