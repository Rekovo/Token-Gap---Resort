using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class GuestMovement : MonoBehaviour
{
    private sealed class Visit
    {
        public ResortNeed Need;
        public int UnitIndex;
        public Vector3 Destination;
        public bool IsServed;
    }

    [Header("Movement")]
    [SerializeField, Min(0.1f)] private float moveSpeed = 2f;
    [SerializeField, Min(0f)] private float stoppingDistance = 0.1f;

    [Header("Visual (SCRUM-80)")]
    [Tooltip("Add multiple guest visual prefabs here. One will be chosen randomly on spawn.")]
    [SerializeField] private Transform[] visualPrefabs; 
    [SerializeField] private Vector3 visualLocalPosition;
    [SerializeField] private Vector3 visualLocalEulerAngles;
    [SerializeField, Min(0.01f)] private float visualScale = 1f;

    public event Action ReturnedToEntrance;
    public event Action StartedWalking;
    public bool IsAtEntrance { get; private set; }

    private readonly Queue<Visit> queuedVisits = new Queue<Visit>();
    private readonly int[] servedUnits = new int[Enum.GetValues(typeof(ResortNeed)).Length];
    private GuestWaveController waveController;
    private Visit currentVisit;
    private Vector3 entrancePosition;
    private Vector3 destination;
    private float groundY;
    private bool isMoving;
    private bool waitingForService;
    private bool isReturning;
    private bool hasStartedWalking;

    // Animation variables (SCRUM-81)
    private Animator animator;
    private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");

    private void Awake()
    {
        if (visualPrefabs == null || visualPrefabs.Length == 0)
            return;

        // Randomly select a guest variant from the array
        Transform selectedPrefab = visualPrefabs[UnityEngine.Random.Range(0, visualPrefabs.Length)];
        
        Transform visual = Instantiate(selectedPrefab, transform, false);
        visual.name = "GuestVisual";
        visual.localPosition = visualLocalPosition; 
        visual.localRotation = Quaternion.Euler(visualLocalEulerAngles);
        visual.localScale = Vector3.Scale(selectedPrefab.localScale, Vector3.one * visualScale);
        AlignVisualFeetToRoot(visual);

        // Get the animator and ensure animations keep playing when temporarily hidden
        animator = visual.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
        }

        SetVisualVisible(false);
    }

    public void BeginWave(Vector3 returnPosition, GuestWaveController controller)
    {
        if (controller == null)
            throw new ArgumentNullException(nameof(controller));

        queuedVisits.Clear();
        Array.Clear(servedUnits, 0, servedUnits.Length);
        currentVisit = null;
        entrancePosition = returnPosition;
        groundY = transform.position.y;
        waveController = controller;
        waveController.OnNeedProgressChanged += HandleNeedProgressChanged;
        isMoving = false;
        waitingForService = false;
        isReturning = false;
        IsAtEntrance = false;
        hasStartedWalking = false;
        
        UpdateAnimationState(false);
    }

    public void QueueVisit(ResortNeed need, int unitIndex, Vector3 visitPoint)
    {
        if (IsAtEntrance || isReturning || waveController == null)
            return;

        if (!hasStartedWalking)
        {
            hasStartedWalking = true;
            SetVisualVisible(true);
            StartedWalking?.Invoke();
        }

        Visit visit = new Visit
        {
            Need = need,
            UnitIndex = unitIndex,
            Destination = visitPoint,
            IsServed = unitIndex < servedUnits[(int)need]
        };
        if (currentVisit == null && !isMoving && !waitingForService)
            StartVisit(visit);
        else
            queuedVisits.Enqueue(visit);
    }

    public void ReturnToEntrance()
    {
        if (IsAtEntrance || isReturning)
            return;

        queuedVisits.Clear();
        currentVisit = null;
        isMoving = false;
        waitingForService = false;
        isReturning = true;
        SetVisualVisible(true); // Ensure visible on return journey
        SetDestination(entrancePosition);
    }

    private void Update()
    {
        if (IsAtEntrance || waveController == null)
            return;

        if (waveController.CurrentState == GuestWaveState.Paused) 
        {
            UpdateAnimationState(false);
            return;
        }

        if (waitingForService)
        {
            UpdateAnimationState(false);
            if (currentVisit != null && currentVisit.IsServed) StartNextVisit();
            return;
        }

        if (!isMoving) 
        {
            UpdateAnimationState(false);
            return;
        }

        UpdateAnimationState(true);

        Vector3 previousPosition = transform.position;
        Vector3 nextPosition = Vector3.MoveTowards(previousPosition, destination, moveSpeed * Time.deltaTime);
        nextPosition.y = groundY;
        transform.position = nextPosition;

        // Absolute destination rotation logic (fixes the high-framerate moonwalking issue)
        Vector3 direction = destination - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        if ((transform.position - destination).sqrMagnitude > stoppingDistance * stoppingDistance)
            return;

        isMoving = false;
        UpdateAnimationState(false); 

        if (isReturning)
        {
            isReturning = false;
            IsAtEntrance = true;
            SetVisualVisible(false);
            ReturnedToEntrance?.Invoke();
        }
        else
        {
            waitingForService = true;
            SetVisualVisible(false); // Hide guest when they arrive at the building
        }
    }

    private void HandleNeedProgressChanged(ResortNeed need, int served, int total)
    {
        servedUnits[(int)need] = served;
        if (currentVisit != null && currentVisit.Need == need && currentVisit.UnitIndex < served)
            currentVisit.IsServed = true;
        foreach (Visit visit in queuedVisits)
            if (visit.Need == need && visit.UnitIndex < served)
                visit.IsServed = true;
        if (waitingForService && currentVisit != null && currentVisit.IsServed)
            StartNextVisit();
    }

    private void StartVisit(Visit visit)
    {
        currentVisit = visit;
        waitingForService = false;
        isMoving = true;
        SetVisualVisible(true); // Show guest when heading to a building
        SetDestination(visit.Destination);
    }

    private void StartNextVisit()
    {
        currentVisit = null;
        waitingForService = false;
        if (queuedVisits.Count > 0)
            StartVisit(queuedVisits.Dequeue());
    }

    private void SetDestination(Vector3 point)
    {
        point.y = groundY;
        destination = point;
        isMoving = true;
        if ((destination - transform.position).sqrMagnitude <= stoppingDistance * stoppingDistance)
        {
            isMoving = false;
            UpdateAnimationState(false);
            if (isReturning)
            {
                isReturning = false;
                IsAtEntrance = true;
                SetVisualVisible(false);
                ReturnedToEntrance?.Invoke();
            }
            else if (currentVisit != null && currentVisit.IsServed)
            {
                StartNextVisit();
            }
            else
            {
                waitingForService = true;
                SetVisualVisible(false); // Hide guest if they instantly arrive
            }
        }
    }

    private void AlignVisualFeetToRoot(Transform visual)
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        float lowestPoint = float.PositiveInfinity;
        foreach (Renderer renderer in renderers)
            lowestPoint = Mathf.Min(lowestPoint, renderer.bounds.min.y);

        visual.position += Vector3.up * (transform.position.y - lowestPoint);
    }

    private void SetVisualVisible(bool visible)
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            renderer.enabled = visible;
    }

    private void UpdateAnimationState(bool isWalking)
    {
        if (animator != null && animator.GetBool(IsWalkingHash) != isWalking)
        {
            animator.SetBool(IsWalkingHash, isWalking);
        }
    }

    private void OnDestroy()
    {
        if (waveController != null)
            waveController.OnNeedProgressChanged -= HandleNeedProgressChanged;
    }
}