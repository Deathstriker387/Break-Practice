using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class LookAtMouse : MonoBehaviour
{
    private Transform m_transform;
    private Camera mainCamera;

    [Header("Movement Settings")]
    public float moveSpeed = 15f;
    private Vector2 targetPosition;
    private bool moving = false;

    [Header("Rope Settings")]
    public Transform playerTransform;
    public float maxLineLength = 10f;
    [SerializeField] private int _numOfRopeSegments = 50;
    [SerializeField] private float _ropeSegmentLength = 0.2f;

    [Header("Rope Physics")]
    [SerializeField] private LayerMask _collisionMask;
    [SerializeField] private float _collisionRadius = 0.1f;
    [SerializeField] private float _constraintStiffness = 0.98f; // How taut the rope is (0.9-1.0 recommended)

    [Header("Rope Constraints")]
    [SerializeField] private int _numOfConstraintRuns = 15; //With 50 segments � 80 runs = 4,000 constraint calculations per frame, Reduce to 10-20 iterations, increase stiffness to compensate
    [SerializeField] private int _collisionSegmentInterval = 2;

    [Header("Collision Settings")]
    public LayerMask grappleMask;
    public string grappleTag = "Grapple";

    [Header("Grapple Pull Settings")]
    [SerializeField] private float pullForce = 25f;
    [SerializeField] private float pullSpeed = 10f;
    [SerializeField] private bool maintainMomentum = true;
    [SerializeField] private bool disablePlayerControlDuringPull = false;

    private Collider2D hookCollider;
    private Collider2D playerCollider;
    private Rigidbody2D hookRb;
    private Rigidbody2D playerRb;
    private bool retracting = false;
    private bool isGrappled = false;
    private bool isPullingPlayer = false;

    // Store player's original state
    private RigidbodyType2D originalBodyType;
    private MonoBehaviour playerMovementScript;

    // Rope system
    private LineRenderer _lineRenderer;
    private List<RopeSegment> _ropeSegments = new List<RopeSegment>();
    private int _activeSegments = 0;

    // Optimization caches
    private Vector2 lastPlayerPos;
    private Vector2 lastHookPos;

    // Non-alloc collision buffer
    private Collider2D[] _collisionBuffer = new Collider2D[8];
    private ContactFilter2D _ropeCollisionFilter;

    // Add a flag for manual player pulling
    private bool isPullingTowardsHook = false; // activated once by 

    public struct RopeSegment
    {
        public Vector2 CurrentPosition;
        public Vector2 OldPosition;

        public RopeSegment(Vector2 pos)
        {
            CurrentPosition = pos;
            OldPosition = pos;
        }
    }

    void Start()
    {
        m_transform = transform;
        mainCamera = Camera.main;
        hookCollider = GetComponent<Collider2D>();
        hookRb = GetComponent<Rigidbody2D>();
        playerCollider = playerTransform.GetComponent<Collider2D>();
        playerRb = playerTransform.GetComponent<Rigidbody2D>();
        targetPosition = hookRb.position;

        // Setup LineRenderer for rope
        _lineRenderer = playerTransform.GetComponent<LineRenderer>();
        if (_lineRenderer == null)
            _lineRenderer = playerTransform.gameObject.AddComponent<LineRenderer>();

        _lineRenderer.positionCount = 0;

        // Initialize position caches
        lastPlayerPos = playerTransform.position;
        lastHookPos = hookRb.position;

        // Ignore collision between hook and player
        if (hookCollider != null && playerCollider != null)
            Physics2D.IgnoreCollision(hookCollider, playerCollider, true);

        // Set hook collider to trigger mode to detect collisions
        if (hookCollider != null)
            hookCollider.isTrigger = true;

        // Validate player rigidbody setup
        if (playerRb == null)
        {
            Debug.LogError("Player Rigidbody2D is missing! Add Rigidbody2D to player.");
        }
        else
        {
            Debug.Log($"Player RB found - BodyType: {playerRb.bodyType}, Mass: {playerRb.mass}, Constraints: {playerRb.constraints}");

            // Store original body type
            originalBodyType = playerRb.bodyType;

            // Ensure player rigidbody is Dynamic
            if (playerRb.bodyType != RigidbodyType2D.Dynamic)
            {
                Debug.LogWarning("Player Rigidbody2D must be Dynamic! Setting it now...");
                playerRb.bodyType = RigidbodyType2D.Dynamic;
            }

            // Try to find player movement script (adjust script name to match yours)
            playerMovementScript = playerTransform.GetComponent<MonoBehaviour>();
            if (playerMovementScript != null)
            {
                Debug.Log($"Found player script: {playerMovementScript.GetType().Name}");
            }
        }

        // Initialize the filter for rope collisions
        _ropeCollisionFilter = new ContactFilter2D();
        _ropeCollisionFilter.SetLayerMask(_collisionMask);
        _ropeCollisionFilter.useTriggers = false;
    }

    private void LAMouse()
    {
        Vector2 direction = targetPosition - (Vector2)m_transform.position; // always face target

        if (direction == Vector2.zero) return;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        m_transform.rotation = Quaternion.AngleAxis(angle - 90f, Vector3.forward);
    }

    void FixedUpdate()
    {
        if (!moving && !isGrappled && _activeSegments == 0)
        {
            hookRb.position = playerTransform.position;
        }
        HandleMovement();

        // Simulate rope physics only when rope exists
        if (_activeSegments > 0)
        {
            for (int i = 0; i < _numOfConstraintRuns; i++)
            {
                ApplyRopeConstraints();

                if (i % _collisionSegmentInterval == 0)
                {
                    HandleRopeCollisions();
                }
            }
        }

        // --- Automatic pull check ---
        if (isGrappled && !isPullingTowardsHook && playerRb != null)
        {
            float distance = Vector2.Distance(playerRb.position, hookRb.position);
            if (distance > maxLineLength)
            {
                isPullingTowardsHook = true; // start automatic pull
                if (disablePlayerControlDuringPull && playerMovementScript != null)
                    playerMovementScript.enabled = false;

                Debug.Log("Automatic pull triggered!");
            }
        }
        // --- End automatic pull check ---

        // Pull player towards hook if active
        if (isPullingTowardsHook && playerRb != null && isGrappled)
        {
            PullPlayerTowardsHook();
            AdjustRopeWhilePulling();
        }

    }

    private void HandleMovement()
    {
        if (!moving) return;

        LAMouse();

        Vector2 currentPos = hookRb.position;
        Vector2 newPos = Vector2.MoveTowards(
        currentPos,
        targetPosition,
        moveSpeed * Time.fixedDeltaTime
        );

        hookRb.MovePosition(newPos);

        // Update rope during both extending and retracting
        if (!retracting && _activeSegments < _numOfRopeSegments)
        {
            UpdateRopeSpawning();
        }
        else if (retracting)
        {
            UpdateRopeRetracting();
        }

        // HARD STOP
        if (Vector2.Distance(newPos, targetPosition) < 0.01f)
        {
            hookRb.MovePosition(targetPosition);
            moving = false;

            // If reached target but not grappled to valid surface, auto-retract
            if (!retracting && !isGrappled)
            {
                StartRetract();
            }
            // Destroy rope when retract completes
            else if (retracting)
            {
                DestroyRope();
                retracting = false;
                isGrappled = false;
                isPullingPlayer = false;
            }
            // Start pulling player when hook is grappled
            else if (isGrappled)
            {
                isPullingPlayer = true;

                // Disable player movement script during pull if enabled
                if (disablePlayerControlDuringPull && playerMovementScript != null)
                {
                    playerMovementScript.enabled = false;
                    Debug.Log("Disabled player control for grapple pull");
                }

                Debug.Log("Starting to pull player!");
            }
        }
    }

    private void UpdateRopeSpawning()
    {
        float distanceTraveled = Vector2.Distance((Vector2)playerTransform.position, hookRb.position);
        int targetSegments = Mathf.Min(
        Mathf.FloorToInt(distanceTraveled / _ropeSegmentLength),
        _numOfRopeSegments
        );

        // Add new segments as hook extends
        while (_activeSegments < targetSegments)
        {
            Vector2 spawnPos;
            if (_ropeSegments.Count == 0)
            {
                spawnPos = playerTransform.position;
            }
            else
            {
                Vector2 lastPos = _ropeSegments[_ropeSegments.Count - 1].CurrentPosition;
                Vector2 directionToHook = (hookRb.position - lastPos).normalized;
                spawnPos = lastPos + directionToHook * _ropeSegmentLength;
            }

            _ropeSegments.Add(new RopeSegment(spawnPos));
            _activeSegments++;
        }

        _lineRenderer.positionCount = _activeSegments;
    }

    private void UpdateRopeRetracting()
    {
        // Keep rope stretched between player and hook as it retracts
        float distanceTraveled = Vector2.Distance((Vector2)playerTransform.position, hookRb.position);
        int targetSegments = Mathf.Max(
        Mathf.FloorToInt(distanceTraveled / _ropeSegmentLength),
        2 // Minimum 2 segments to show rope
        );

        // Remove segments from the hook end as it gets closer
        while (_activeSegments > targetSegments && _activeSegments > 2)
        {
            _ropeSegments.RemoveAt(_ropeSegments.Count - 1);
            _activeSegments--;
        }

        _lineRenderer.positionCount = _activeSegments;
    }

    private void DestroyRope()
    {
        _ropeSegments.Clear();
        _activeSegments = 0;
        _lineRenderer.positionCount = 0;
    }

    private void ApplyRopeConstraints()
    {
        if (_activeSegments == 0) return;

        // First segment anchored to player
        RopeSegment firstSegment = _ropeSegments[0];
        firstSegment.CurrentPosition = playerTransform.position;
        _ropeSegments[0] = firstSegment;

        // Last segment anchored to hook
        if (_activeSegments > 1)
        {
            RopeSegment lastSegment = _ropeSegments[_activeSegments - 1];
            lastSegment.CurrentPosition = hookRb.position;
            _ropeSegments[_activeSegments - 1] = lastSegment;
        }

        // Apply distance constraints to keep rope taut
        for (int i = 0; i < _activeSegments - 1; i++)
        {
            RopeSegment currentSeg = _ropeSegments[i];
            RopeSegment nextSeg = _ropeSegments[i + 1];

            float dist = (currentSeg.CurrentPosition - nextSeg.CurrentPosition).magnitude;
            float difference = dist - _ropeSegmentLength;

            Vector2 changeDir = (currentSeg.CurrentPosition - nextSeg.CurrentPosition).normalized;
            Vector2 changeVector = changeDir * difference * _constraintStiffness;

            // Don't move first and last segments (anchored)
            if (i == 0)
            {
                nextSeg.CurrentPosition += changeVector;
            }
            else if (i == _activeSegments - 2)
            {
                currentSeg.CurrentPosition -= changeVector;
            }
            else
            {
                currentSeg.CurrentPosition -= changeVector * 0.5f;
                nextSeg.CurrentPosition += changeVector * 0.5f;
            }

            _ropeSegments[i] = currentSeg;
            _ropeSegments[i + 1] = nextSeg;
        }
    }

    private void HandleRopeCollisions()
    {
        for (int i = 1; i < _activeSegments - 1; i++)
        {
            RopeSegment segment = _ropeSegments[i];

            int hitCount = Physics2D.OverlapCircle(
            segment.CurrentPosition,
            _collisionRadius,
            _ropeCollisionFilter,
            _collisionBuffer
            );

            for (int j = 0; j < hitCount; j++)
            {
                Collider2D collider = _collisionBuffer[j];

                Vector2 closestPoint = collider.ClosestPoint(segment.CurrentPosition);
                float distance = Vector2.Distance(segment.CurrentPosition, closestPoint);

                if (distance < _collisionRadius)
                {
                    Vector2 normal = segment.CurrentPosition - closestPoint;

                    if (normal.sqrMagnitude < 0.0001f)
                    {
                        normal = segment.CurrentPosition - (Vector2)collider.bounds.center;
                    }

                    normal.Normalize();
                    float depth = _collisionRadius - distance;

                    segment.CurrentPosition += normal * depth;
                }
            }

            _ropeSegments[i] = segment;
        }
    }

    private void DrawRope()
    {
        if (_activeSegments == 0) return;

        Vector3[] ropePositions = new Vector3[_activeSegments];
        for (int i = 0; i < _activeSegments; i++)
        {
            ropePositions[i] = _ropeSegments[i].CurrentPosition;
        }

        _lineRenderer.SetPositions(ropePositions);
    }

    private void StartRetract()
    {
        targetPosition = playerTransform.position;
        retracting = true;
        moving = true;
        isPullingPlayer = false;

        // Re-enable player control
        if (disablePlayerControlDuringPull && playerMovementScript != null)
        {
            playerMovementScript.enabled = true;
            Debug.Log("Re-enabled player control");
        }
    }

    private void PullPlayerTowardsHook()
    {
        float currentDistance = Vector2.Distance(playerRb.position, hookRb.position);
        float minDistance = 3f;

        if (currentDistance <= minDistance)
        {
            isPullingPlayer = false;
            Debug.Log("Pull stopped - reached minimum distance");
            return;
        }

        Vector2 direction = (hookRb.position - playerRb.position).normalized;

        // SIMPLE AND EFFECTIVE APPROACH:
        // Use direct force towards the hook with speed limiting

        // Calculate desired speed based on distance
        float desiredSpeed = Mathf.Min(pullSpeed, (currentDistance - minDistance) * 2f);

        // Get current velocity component towards the hook
        float currentSpeedTowardsHook = Vector2.Dot(playerRb.linearVelocity, direction);

        // Calculate how much speed we need to add
        float speedNeeded = desiredSpeed - currentSpeedTowardsHook;

        // Apply force to achieve the needed speed
        if (speedNeeded > 0)
        {
            Vector2 force = direction * speedNeeded * playerRb.mass * 5f; // Multiplier for responsiveness
            playerRb.AddForce(force, ForceMode2D.Force);

            Debug.Log($"Pulling - Dist: {currentDistance}, Speed: {desiredSpeed}, Force: {force.magnitude}");
        }
    }


    private void AdjustRopeWhilePulling()
    {
        // Adjust rope segments as player moves closer to hook
        float distanceToHook = Vector2.Distance(playerTransform.position, hookRb.position);
        int targetSegments = Mathf.Max(
        Mathf.FloorToInt(distanceToHook / _ropeSegmentLength),
        2 // Minimum 2 segments
        );

        // FIX: Remove from hook end (not player end)
        while (_activeSegments > targetSegments && _activeSegments > 2)
        {
            _ropeSegments.RemoveAt(_ropeSegments.Count - 1); // Remove from END (hook side)
            _activeSegments--;
        }

        _lineRenderer.positionCount = _activeSegments;
    }


    // Collision detection for grapple points
    private void OnTriggerEnter2D(Collider2D other)
    {
        // Check if hook hit a valid grapple surface
        if (((1 << other.gameObject.layer) & grappleMask) != 0 || other.CompareTag(grappleTag))
        {
            isGrappled = true;
            Debug.Log("Hook grappled to: " + other.gameObject.name);
        }
        else if (!retracting)
        {
            // Hit invalid surface while extending - auto retract
            isGrappled = false;
            Debug.Log("Hook hit invalid surface: " + other.gameObject.name + ", retracting...");
            StartRetract();
        }
    }

    void Update()
    {
        // draw rope every frame
        DrawRope();

        // LEFT CLICK to Shoot
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            // Only allow shooting if not currently grappled or retracting
            if (!isGrappled && !retracting)
            {
                Vector2 mousePos = Mouse.current.position.ReadValue();
                Vector2 clickedPos = mainCamera.ScreenToWorldPoint(mousePos);
                Vector2 dir = clickedPos - (Vector2)playerTransform.position;

                if (dir.magnitude > maxLineLength)
                    clickedPos = (Vector2)playerTransform.position + dir.normalized * maxLineLength;

                targetPosition = clickedPos;
                moving = true;
                isGrappled = false; // Reset grapple state
                isPullingPlayer = false; // Reset pull state

                // Clear previous rope
                DestroyRope();
            }
        }

        // RIGHT CLICK to Retract (or release while being pulled)
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            if (!retracting)
            {
                StartRetract();
            }
        }

        // DEBUG: Press SPACE to manually test pull force
        if (Keyboard.current.spaceKey.wasPressedThisFrame && playerRb != null)
        {
            Vector2 testForce = Vector2.up * 100f;
            playerRb.AddForce(testForce, ForceMode2D.Force);
            Debug.Log($"DEBUG: Applied test force {testForce} - Current velocity: {playerRb.linearVelocity}");
        }

        // Press X to start one-time pull
        if (Keyboard.current.xKey.wasPressedThisFrame)
        {
            if (isGrappled && !isPullingTowardsHook)
            {
                isPullingTowardsHook = true;

                // Optionally disable player movement during pull
                if (disablePlayerControlDuringPull && playerMovementScript != null)
                    playerMovementScript.enabled = false;

                Debug.Log("Player started moving towards hook!");
            }
        }

        // RIGHT CLICK to retract (or release while being pulled)
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            if (!retracting)
            {
                StartRetract();

                // Stop pulling
                isPullingTowardsHook = false;

                // Re-enable player movement
                if (disablePlayerControlDuringPull && playerMovementScript != null)
                    playerMovementScript.enabled = true;

                Debug.Log("Pull canceled by retract!");
            }
        }

    }
}