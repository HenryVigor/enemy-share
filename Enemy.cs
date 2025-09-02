using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

using DmgInst = AtkUtil.DmgInst;

public class Enemy : BehaviourFSM {
    ////////// Default Values //////////////////////////////////////////////////
    
    /// <summary>Default max health</summary>
    const int DEF_MAX_HEALTH = 1;

    /// <summary>Default movement speed</summary>
    const float DEF_SPEED = 3.5f;

    /// <summary>Default knockback resistance</summary>
    const float DEF_KB_RESIST = 1f;

    ////////////////////////////////////////////////////////////////////////////
    ////////// Editor Options & Related Variables //////////////////////////////
    ////////// General Stats /////////////////////////////////////////

    [SerializeField, Min(1)] int _MaxHealth = DEF_MAX_HEALTH;
    protected int MaxHealth {
        get => _MaxHealth;
        set => _MaxHealth = value > 0 ? value : DEF_MAX_HEALTH;
    }

    int _Health;
    protected int Health {
        get => _Health;
        set {
            if (value < 0) value = 0;
            else if (value > MaxHealth) value = MaxHealth;
            _Health = value;
        }
    }

    [SerializeField, Min(0f)] float _Speed = DEF_SPEED;
    /// <summary>Movement speed</summary>
    /// <value>Must be greater than or equal to 0</value>
    protected float Speed {
        get => _Speed;
        set {
            _Speed = value >= 0f ? value : DEF_SPEED;
            NavAgent.speed = _Speed;
        }
    }

    [SerializeField, Min(0f)] float _KbResist = 1f;
    protected float KbResist {
        get => _KbResist;
        set => _KbResist = value > 0f ? value : 1f;
    }

    //////////////////////////////////////////////////////////////////
    ////////// Handling Contact with Player //////////////////////////

    /// <summary>Damage instance for contact damage</summary>
    protected DmgInst ContactDI { get; private set; }

    [SerializeField, Min(0)] float _ContactDamage = 1;
    protected float ContactDamage {
        get => _ContactDamage;
        set {
            DmgInst di = ContactDI;
            _ContactDamage = di.Damage = value;
            ContactDI = di;
        }
    }

    [SerializeField, Min(0f)] float _KbStr = 1f;
    protected float KbStr {
        get => _KbStr;
        set {
            DmgInst di = ContactDI;
            _KbStr = di.Knockback.Strength = value;
            ContactDI = di;
        }
    }

    //////////////////////////////////////////////////////////////////
    ////////// Phases ////////////////////////////////////////////////

    /// <summary>Total attack phases to search for on initialization</summary>
    [SerializeField, Min(1)] int NumberOfPhases = 1;

    /// Phase Point List
    /// <summary>
    ///     Health points as percentages for changing attack phases
    /// </summary>
    List<float> PhasePoints = new();

    /// <summary>List of attack sets for each phase</summary>
    List<List<Type>> Phases = new();

    MethodInfo PhaseShiftCheck = null;

    int _PhaseIndex = 0;
    /// <summary>Current phase as list index</summary>
    int PhaseIndex {
        get => _PhaseIndex;
        set => CurrentPhase = (_PhaseIndex = value) + 1;
    }

    /// <summary>Current phase, 1 being starting phase</summary>
    /// <value>Set automatically by <see cref="PhaseIndex"/></value>
    protected int CurrentPhase { get; private set; } = 1;

    //////////////////////////////////////////////////////////////////

    /// <summary>Starting idle state status</summary>
    [Tooltip("If false on start, Off state will be skipped.")]
    [SerializeField] bool Idle = true;

    /// <summary>Whether to pathfind to the player in Main state</summary>
    [Tooltip("If true, enemy will navigate to the player while in Main state.")]
    [SerializeField] bool FollowPlayerInMainState = false;

    ////////////////////////////////////////////////////////////////////////////
    ////////// Attacks & States ////////////////////////////////////////////////

    /// <summary>Alive state status</summary>
    bool Alive = true;

    /// <summary>Current attacks</summary>
    List<Type> Attacks = new();

    int _AttackIndex = 0;
    /// Attack index pointer
    /// <summary>
    ///     Index pointer to current attack in <see cref="Attacks"/>
    /// </summary>
    /// <value>Remains between 0 and <see cref="Attacks.Count"/></value>
    int AttackIndex {
        get => _AttackIndex;
        set {
            if (Attacks.Count > 0) {
                while (value >= Attacks.Count) value -= Attacks.Count;
                while (value < 0) value += Attacks.Count;
            }
            _AttackIndex = value;
        }
    }

    /// <summary>Current attack state object</summary>
    /// <returns>
    ///     Active state as <see cref="Attack"/> if attacking. Null if not.
    /// </returns>
    protected Attack ActiveAttackState {
        get => IsAttacking() ? (Attack)ActiveState : null;
    }

    /// <summary>Current enemy state object</summary>
    /// <returns>
    ///     <see cref="BehaviourFSM.ActiveState"/> as <see cref="EnemyState"/>
    /// </returns>
    protected EnemyState ActiveEnemyState { get => (EnemyState)ActiveState; }

    ////////////////////////////////////////////////////////////////////////////
    ////////// Component Pointers //////////////////////////////////////////////

    /// <summary>Pointer to <see cref="Rigidbody2D"/> component</summary>
    protected Rigidbody2D rb { get; private set; }

    /// <summary>Pointer to <see cref="NavMeshAgent"/> component</summary>
    NavMeshAgent NavAgent;

    ////////////////////////////////////////////////////////////////////////////
    ////////// Next States for Built-In State Transitions //////////////////////

    /// Off-Main Transition State
    /// <summary>
    ///     Transition state type used in default <see cref="Trans_Off"/> logic
    ///     when <see cref="Idle"/> is false.<br/>
    ///     Typeof <see cref="Main"/> by default.
    /// </summary>
    protected Type TDef_Off2Main = typeof(Main);

    ////////////////////////////////////////////////////////////////////////////
    ////////// Unity Behavior Methods //////////////////////////////////////////

    void Awake() {
        Initialize();
        OnAwake();
    }

    protected sealed override void OnUpdate() {
        PhaseShift();
    }

    void OnCollisionStay2D(Collision2D collision) {
        AtkUtil.TryHitPlayer(collision.collider, ContactDI);
    }

    ////////////////////////////////////////////////////////////////////////////
    ////////// Built-In Behavioral States //////////////////////////////////////

    /// <summary>Starting idle state</summary>
    class Off : EnemyState {
        public override void Init() {
            GrantsInvincibility = true;
            Machine.Init_Off();
        }

        public override void Enter() => Machine.Enter_Off();
        public override void Update() => Machine.Update_Off();
        public override void Exit() => Machine.Exit_Off();
        protected override Type StateTransitions() => Machine.Trans_Off();
    }

    /// <summary>Main state</summary>
    class Main : EnemyState {
        public override void Init() => Machine.Init_Main();

        public override void Enter() {
            if (Machine.FollowPlayerInMainState) FollowPlayer();
            Machine.Enter_Main();
        }

        public override void Update() => Machine.Update_Main();

        public override void Exit() {
            if (Machine.FollowPlayerInMainState) StopFollow();
            Machine.Exit_Main();
        }

        protected override Type StateTransitions() => Machine.Trans_Main();
    }

    /// <summary>Death state</summary>
    class Death : EnemyState {
        public override void Enter() {
            Machine.Enter_Death();
            Destroy(Machine.gameObject);
        }
    }

    ////////////////////////////////////////////////////////////////////////////
    ////////// Enemy State Type Definitions ////////////////////////////////////

    /// <summary>Base type for all enemy states</summary>
    protected class EnemyState : State<Enemy> {
        /// <summary>Whether enemy is invincible while in this state</summary>
        /// <value>
        ///     If set to true, enemy will not take damage while in this state
        /// </value>
        public bool GrantsInvincibility { get; protected set; } = false;

        /// Pointers to outer members
        protected int CurrentPhase { get => Machine.CurrentPhase; }
        protected Transform transform { get => Machine.transform; }
        protected Rigidbody2D rb { get => Machine.rb; }

        /// <summary>Pointer to this enemy's contact damage instance</summary>
        /// <value>Returns and sets <see cref="Enemy.ContactDI"/></value>
        protected DmgInst ContactDI {
            get => Machine.ContactDI;
            set => Machine.ContactDI = value;
        }

        public sealed override Type Transitions() {
            Type nextState = Machine.PriorityStateTransitions();
            if (nextState != null) return nextState;
            return StateTransitions();
        }

        ////////// Pathfinding /////////////////////////////////////////////////

        /// <summary>Coroutine for following an object</summary>
        Coroutine FollowRoutine = null;

        /// <summary>Sets pathfinding destination</summary>
        /// <param name="position">Destination to set</param>
        protected void NavTo(Vector2 position) {
            if (Machine.Alive) Machine.NavAgent.SetDestination(position);
        }

        /// <summary>Cancels navigation path</summary>
        protected void StopNav() => Machine.NavAgent.ResetPath();

        /// Follow Transform
        /// <summary>
        ///     Begins pathfinding toward a transform until stopped or time
        ///     expires
        /// </summary>
        /// <param name="objT">Transform to follow</param>
        /// <param name="t">Amount of time in seconds to follow for</param>
        protected void Follow(Transform objT, float t) {
            PreFollow();
            FollowRoutine = Machine.StartCoroutine(TimedFollow(objT, t));
        }

        /// Follow GameObject
        /// <summary>
        ///     Begins pathfinding toward a GameObject's transform until stopped
        ///     or time expires
        /// </summary>
        /// <param name="obj">GameObject to follow</param>
        /// <param name="t">Time in seconds to follow for</param>
        protected void Follow(GameObject obj, float t) {
            Follow(obj.transform, t);
        }

        /// Follow Transform (Continuous)
        /// <summary>
        ///     Begins continuous pathfinding toward a transform until stopped
        /// </summary>
        /// <param name="objT">Transform to follow</param>
        protected void Follow(Transform objT) {
            PreFollow();
            FollowRoutine = Machine.StartCoroutine(InfFollow(objT));
        }

        /// Follow GameObject (Continuous)
        /// <summary>
        ///     Begins continuous pathfinding toward a GameObject's transform
        ///     until stopped
        /// </summary>
        /// <param name="obj">GameObject to follow</param>
        protected void Follow(GameObject obj) => Follow(obj.transform);

        /// Follow Player
        /// <summary>
        ///     Begins pathfinding toward player until stopped or time expires
        /// </summary>
        /// <param name="t">Time in seconds to follow for</param>
        protected void FollowPlayer(float t) => Follow(Player.Obj.transform, t);

        /// <summary>Begins pathfinding toward player until stopped</summary>
        protected void FollowPlayer() => Follow(Player.Obj.transform);

        /// <summary>Stops current follow routine</summary>
        protected void StopFollow() {
            if (FollowRoutine != null) {
                Machine.StopCoroutine(FollowRoutine);
                PostFollow();
            }
        }

        /// Pre Follow
        /// <summary>
        ///     Run before starting a follow routine. Stops current follow
        ///     routine if it exists
        /// </summary>
        void PreFollow() {
            if (FollowRoutine != null) Machine.StopCoroutine(FollowRoutine);
        }

        /// Post Follow
        /// <summary>
        ///     Run after a follow routine ends or is stopped. Dereferences
        ///     follow routine and stops navigation
        /// </summary>
        void PostFollow() {
            FollowRoutine = null;
            StopNav();
        }

        /// Timed Follow Routine
        /// <summary>
        ///     Sets pathfinding destination to a transform's position each
        ///     frame until time expires
        /// </summary>
        /// <param name="objT">Transform to follow</param>
        /// <param name="t">Time in seconds to follow for</param>
        IEnumerator TimedFollow(Transform objT, float t) {
            for (; t > 0f; t -= Time.deltaTime) {
                NavTo(objT.position);
                yield return null;
            }
            PostFollow();
        }

        /// Infinite Follow Routine
        /// <summary>
        ///     Sets pathfinding destination to a transform's position each
        ///     frame indefinitely
        /// </summary>
        /// <param name="objT">Transform to follow</param>
        IEnumerator InfFollow(Transform objT) {
            while (true) {
                NavTo(objT.position);
                yield return null;
            }
        }

        ////////////////////////////////////////////////////////////////////////
        ////////////// Functions Referenced from Enclosing Class ///////////////

        /// <summary>Angle to player</summary>
        /// <returns>
        ///     The angle in degrees of the direction from enemy to player
        /// </returns>
        public float AngleToPlayer() => Machine.AngleToPlayer();

        /// <summary>Distance to player</summary>
        /// <returns>The distance between enemy and player</returns>
        public float DistToPlayer() => Machine.DistToPlayer();

        /// <summary>Vector to player</summary>
        /// <returns>The raw 2D vector from enemy to player</returns>
        public Vector2 VectToPlayer() => Machine.VectToPlayer();

        /// <summary>Direction to player</summary>
        /// <returns>The normalized 2D direction from enemy to player</returns>
        public Vector2 DirToPlayer() => Machine.DirToPlayer();

        protected Cooldown AddCooldown(float duration = 1f) {
            return Machine.AddCooldown(duration);
        }

        protected Enemy SpawnAdd(Enemy orig, Vector2 pos) {
            return Machine.SpawnAdd(orig, pos);
        }

        ////////// SpawnProj Variants //////////////////////////////////////////

        protected Projectile SpawnProj(
            Projectile.SpawnPkg sp, float? dirOverride = null,
            bool spawnHere = true
        ) => Machine.SpawnProj(sp, dirOverride, spawnHere);

        protected Projectile SpawnProj(
            Projectile.SpawnPkg sp, Vector2 posOverride,
            float? dirOverride = null
        ) => Machine.SpawnProj(sp, posOverride, dirOverride);

        ////////////////////////////////////////////////////////////////////////

        /// State-Specific Transitions
        /// <summary>
        ///     Logic for which state to switch to and when. Returned by
        ///     <see cref="Transitions"/> if
        ///     <see cref="PriorityStateTransitions"/> returns null.
        /// </summary>
        /// <returns>Always returns null by default</returns>
        protected virtual Type StateTransitions() => null;

        ////////// Movement Animation //////////////////////////////////////////

        /// <summary>Sets enemy velocity for a given duration</summary>
        /// <param name="vel">Velocity to set</param>
        /// <param name="dur">Duration</param>
        protected void AnimateMovement(
            Vector2 vel, float dur, AnimationCurve sCurve = null
        ) {
            MakeNewMoveAnimation(vel, dur, sCurve);
        }

        /// Animate Movement (Generic)
        /// <summary>
        ///     Sets enemy velocity using derived <see cref="MoveAnimation"/>
        ///     type for its set duration
        /// </summary>
        /// <typeparam name="T">
        ///     <see cref="MoveAnimation"/> derivation to use
        /// </typeparam>
        protected void AnimateMovement<T>() where T : MoveAnimation {
            Machine.AddComponent<T>();
        }

        /// Interpolate Movement
        /// <summary>
        ///     Interpolates enemy velocity over given curves for a given
        ///     duration
        /// </summary>
        /// <param name="xVel">
        ///     Curve representing x-axis velocity over time
        /// </param>
        /// <param name="yVel">
        ///     Curve representing y-axis velocity over time
        /// </param>
        /// <param name="dur">Duration</param>
        protected void InterpMovement(
            AnimationCurve xVel, AnimationCurve yVel, float dur
        ) {
            MoveWithCurves mi = Machine.AddComponent<MoveWithCurves>();
            mi.XVel = xVel;
            mi.YVel = yVel;
            mi.Duration = dur;
        }

        /// Animate Movement (Routine)
        /// <summary>
        ///     Same as <see cref="AnimateMovement"/>, but creates a routine
        ///     that waits for the given duration
        /// </summary>
        /// <param name="velocity">Velocity to set</param>
        /// <param name="duration">Duration</param>
        protected IEnumerator AnimMove(
            Vector2 velocity, float duration, AnimationCurve speedCurve = null
        ) {
            MakeNewMoveAnimation(velocity, duration, speedCurve);
            yield return new WaitForSeconds(duration);
        }

        /// Animate Movement (Generic Routine)
        /// <summary>
        ///     Same as <see cref="AnimateMovement{T}"/>, but creates a routine
        ///     that waits for the given duration
        /// </summary>
        /// <typeparam name="T">
        ///     <see cref="MoveAnimation"/> derivation to use
        /// </typeparam>
        protected IEnumerator AnimMove<T>() where T : MoveAnimation {
            T ma = MakeNewMoveAnimation<T>();
            yield return new WaitForSeconds(ma.Duration);
        }

        /// Interpolate Movement (Routine)
        /// <summary>
        ///     Same as <see cref="InterpMovement"/>, but creates a routine that
        ///     waits for the given duration
        /// </summary>
        /// <param name="xVel">
        ///     Curve representing x-axis velocity over time
        /// </param>
        /// <param name="yVel">
        ///     Curve representing y-axis velocity over time
        /// </param>
        /// <param name="dur">Duration</param>
        protected IEnumerator Merp(
            AnimationCurve xVel, AnimationCurve yVel, float dur
        ) {
            InterpMovement(xVel, yVel, dur);
            yield return new WaitForSeconds(dur);
        }

        /// Make New Move Animation
        /// <summary>
        ///     Makes a new <see cref="MoveAnimation"/> component with given
        ///     values
        /// </summary>
        /// <param name="velocity">Velocity to set</param>
        /// <param name="duration">Duration for move animation</param>
        /// <param name="speedCurve">
        ///     Curve of values to multiply velocity by over time
        /// </param>
        MoveAnimation MakeNewMoveAnimation(
            Vector2 velocity, float duration, AnimationCurve speedCurve
        ) {
            MoveAnimation ma = Machine.AddComponent<MoveAnimation>();
            ma.EnclosingState = this;
            ma.Velocity = velocity;
            ma.Duration = duration;
            ma.SpeedCurve = speedCurve;
            return ma;
        }

        /// Make New Move Animation (Generic)
        /// <summary>
        ///     Makes a new derived <see cref="MoveAnimation"/> component
        /// </summary>
        /// <typeparam name="T">
        ///     The derived <see cref=MoveAnimation"/> type
        /// </typeparam>
        T MakeNewMoveAnimation<T>() where T : MoveAnimation {
            T ma = Machine.AddComponent<T>();
            ma.EnclosingState = this;
            ma.Setup();
            return ma;
        }

        /// <summary>Base class for movement animation types</summary>
        protected abstract class MoveAnimation_Base : MonoBehaviour {
            float _Duration = 0f;
            /// <summary>Duration for velocity control</summary>
            public float Duration {
                get => _Duration;
                set => _Duration = _Duration == 0f ? value : _Duration;
            }

            /// <summary>Pointer to enemy's <see cref="Rigidbody2D"/></summary>
            protected Rigidbody2D rb { get; private set; }

            void Awake() {
                rb = GetComponent<Rigidbody2D>();
            }

            void Start() {
                PreRoutine();
                StartCoroutine(Routine());
            }

            IEnumerator Routine() {
                yield return MoveRoutine();
                Destroy(this);
            }

            /// <summary>Main routine for animating movement</summary>
            protected abstract IEnumerator MoveRoutine();

            /// Pre-Routine
            /// <summary>
            ///     Runs before <see cref="MoveRoutine"/> is started. Empty by
            ///     default.
            /// </summary>
            protected virtual void PreRoutine() { }
        }

        /// Movement Animation
        /// <summary>
        ///     <see cref="MonoBehaviour"/> component that controls enemy's
        ///     velocity for a given duration, then destroys itself
        /// </summary>
        protected class MoveAnimation : MoveAnimation_Base {
            /// Velocity
            /// <summary>
            ///     Velocity to set each frame when using default settings
            /// </summary>
            public Vector2 Velocity = Vector2.zero;

            AnimationCurve _SpeedCurve = null;
            /// <summary>
            ///     Curve with which to interpolate the magnitude of the
            ///     velocity over time. Defaults to a constant line always
            ///     equating to 1.
            /// </summary>
            /// <value>Set only if null</value>
            public AnimationCurve SpeedCurve {
                get => _SpeedCurve;
                set => _SpeedCurve ??= value;
            }

            EnemyState _EnclosingState;
            /// <summary>
            ///     Pointer to the enclosing <see cref="EnemyState"/>
            /// </summary>
            /// <value>Set by enclosing state on component creation</value>
            public EnemyState EnclosingState {
                get => _EnclosingState;
                set => _EnclosingState ??= value;
            }

            protected sealed override void PreRoutine() {
                SpeedCurve = AnimationCurve.Constant(0f, 1f, 1f);
            }

            /// Move Routine
            /// <summary>
            ///     Sets enemy's velocity to the returned value of
            ///     <see cref="CurrentVel"/> each frame until
            ///     <see cref="MoveAnimation_Base.Duration"/> seconds have
            ///     elapsed, then destroys this component
            /// </summary>
            protected sealed override IEnumerator MoveRoutine() {
                for (float t = 0f; t <= Duration; t += Time.deltaTime) {
                    rb.velocity =
                        CurrentVel() * SpeedCurve.Evaluate(t / Duration)
                    ;
                    yield return null;
                }
                rb.velocity = CurrentVel() * SpeedCurve.Evaluate(1f);
            }

            /// Setup
            /// <summary>
            ///     Called by <see cref="MakeNewMoveAnimation{T}"/> to
            ///     allow initialization of values after
            ///     <see cref="EnclosingState"/> is set. Empty by default.
            /// </summary>
            public virtual void Setup() { }

            /// Current Velocity
            /// <summary>
            ///     Velocity to set each frame<br>
            ///     Returns value of <see cref="Velocity"/> by default<br>
            ///     Allows for dynamic runtime velocity changes over the
            ///     duration of this movement animation
            /// </summary>
            /// <returns>Velocity to set for a given frame</returns>
            protected virtual Vector2 CurrentVel() => Velocity;
        }

        /// Move Animation (Generic)
        /// <summary>
        ///     Generic Movement Animation with more accurate pointer to
        ///     enclosing state
        /// </summary>
        /// <typeparam name="T"></typeparam>
        protected class MoveAnimation<T> : MoveAnimation where T : EnemyState {
            protected T Up { get => (T)EnclosingState; }
        }

        /// Movement Interpolation
        /// <summary>
        ///     <see cref="MonoBehaviour"/> component that interpolates enemy's
        ///     velocity over given curves for a given duration, then destroys
        ///     itself
        /// </summary>
        class MoveWithCurves : MoveAnimation_Base {
            /// Curves representing x- and y-axis velocities over time
            public AnimationCurve XVel, YVel;

            /// Move Routine
            /// <summary>
            ///     Interpolates enemy's velocity over the values of
            ///     <see cref="XVel"/> and <see cref="YVel"/> each frame until
            ///     <see cref="Dur"/> seconds have elapsed, then destroys this
            ///     component
            /// </summary>
            protected sealed override IEnumerator MoveRoutine() {
                for (float t = 0f; t <= Duration; t += Time.deltaTime) {
                    float p = t / Duration;
                    rb.velocity = new(XVel.Evaluate(p), YVel.Evaluate(p));
                    yield return null;
                }
                rb.velocity = new(XVel.Evaluate(1f), YVel.Evaluate(1f));
            }
        }

        ////////////////////////////////////////////////////////////////////////

    }

    /// Enemy State (Generic)
    /// <summary>
    ///     Same as <see cref="EnemyState"/> with a pointer to the derived enemy
    ///     object
    /// </summary>
    /// <typeparam name="T"></typeparam>
    protected class EnemyState<T> : EnemyState where T : Enemy {
        public T Up { get => (T)Machine; }

        new protected class MoveAnimation<T2> : MoveAnimation
            where T2 : EnemyState<T> {
            public T2 Up { get => (T2)EnclosingState; }
        }
    }

    /// <summary>Base enemy state for attacks</summary>
    protected class Attack : EnemyState {
        /// Wait Time
        /// <summary>
        ///     Cooldown time before this attack can be performed again
        /// </summary>
        protected float WaitTime = 0f;

        /// Interrupt State
        /// <summary>
        ///     State to switch to if this attack is interrupted.
        ///     Set to <see cref="Main"/> by default.
        /// </summary>
        protected Type InterruptState = typeof(Main);

        /// Interrupt Status
        /// <summary>
        ///     Set true for the remainder of attack if interrupted
        /// </summary>
        protected bool Interrupted { get; private set; } = false;

        /// <summary>Whether this attack is still being performed</summary>
        bool Performing = false;

        /// <summary>Pointer to attack coroutine for interruption</summary>
        IEnumerator AttackScript;

        /// <summary>Cooldown between attack usage</summary>
        Cooldown AttackCooldown;

        public sealed override void Init() {
            PreInit();
            AttackCooldown = AddCooldown(WaitTime);
        }

        public sealed override void Enter() {
            Machine.StartCoroutine(PerformAttack());
        }

        public sealed override void Update() {
            if (Interruption()) Interrupt();
        }

        public sealed override void Exit() {
            AttackCooldown.StartCooldown();
        }

        protected override Type StateTransitions() {
            if (!Performing) {
                if (Interrupted) {
                    Interrupted = false;
                    return InterruptState;
                }
                return typeof(Main);
            }
            return null;
        }

        /// <summary>Interrupts this attack if it is being performed</summary>
        public void Interrupt() {
            if (Performing) {
                Interrupted = true;
                Machine.StopCoroutine(AttackScript);
                PostAttack();
            }
        }

        /// <summary>Whether or not this attack can be performed</summary>
        /// <returns>
        ///     True if both <see cref="AttackCooldown"/> is ready and
        ///     <see cref="PerformRules"/> returns true.<br/>
        ///     False otherwise.
        /// </returns>
        public bool CanPerform() => AttackCooldown.Ready && PerformRules();

        /// <summary>Procedure called before attack routine</summary>
        void PreAttack() {
            Performing = true;
            AttackScript = Script();
        }

        /// <summary>Procedure called after attack routine</summary>
        void PostAttack() => Performing = false;

        /// Perform Attack
        /// <summary>
        ///     Attack routine parent function. Called in <see cref="Enter"/>.
        /// </summary>
        IEnumerator PerformAttack() {
            PreAttack();
            yield return Machine.StartCoroutine(AttackScript);
            PostAttack();
        }

        /// Pre-Init
        /// <summary>
        ///     Called at the start of <see cref="Init"/>. Override for derived
        ///     initialization.
        /// </summary>
        protected virtual void PreInit() { }

        /// Perform Rules
        /// <summary>
        ///     Logic for whether or not this attack can be performed.
        /// </summary>
        /// <returns>True by default</returns>
        protected virtual bool PerformRules() => true;

        /// Interruption Rules
        /// <summary>
        ///     Interruption logic. Checked in <see cref="Update"/>. This attack
        ///     will be interrupted if this returns true.
        /// </summary>
        /// <returns>False by default</returns>
        protected virtual bool Interruption() => false;

        /// Attack Script
        /// <summary>
        ///     Main routine script for this attack. Breaks by default.
        /// </summary>
        protected virtual IEnumerator Script() { yield break; }
    }

    /// Attack (Generic)
    /// <summary>
    ///     Same as <see cref="Attack"/>, but with a pointer to the derived
    ///     enemy object
    /// </summary>
    /// <typeparam name="T">Derived <see cref="Enemy"/> type</typeparam>
    protected class Attack<T> : Attack where T : Enemy {
        public T Up { get => (T)Machine; }

        new protected class MoveAnimation<T2> : MoveAnimation
            where T2 : Attack<T> {
            public T2 Up { get => (T2)EnclosingState; }
        }
    }

    ////////////////////////////////////////////////////////////////////////////

    /// <summary>Registers a combative hit against this enemy</summary>
    /// <param name="di">Damage instance for hit</param>
    public void RegisterHit(DmgInst di) {
        print((int)di.Damage);
        if (!ActiveEnemyState.GrantsInvincibility) {
            if (Health > 0) OnHit(di);
            if (Health == 0) OnZeroHealth();
        }
    }

    /// <summary>Engages enemy in combat</summary>
    public void Engage() => Idle = false;

    /// Add Attacks
    /// <summary>
    ///     Adds attacks to current moveset if they are not already
    /// </summary>
    /// <param name="attacks">List of attack state types to add</param>
    protected void AddAttacks(List<Type> attacks) {
        foreach (Type attack in attacks) {
            if (
                attack.IsSubclassOf(typeof(Attack)) && !Attacks.Contains(attack)
            ) {
                Attacks.Add(attack);
                LoadState(attack);
            }
        }
    }

    /// Clear Attacks
    /// <summary>
    ///     Clears <see cref="Attacks"/> and unloads its attack states
    /// </summary>
    protected void ClearAttacks() {
        for (int i = Attacks.Count - 1; i >= 0; i--) {
            RemoveState(Attacks[i]);
            Attacks.RemoveAt(i);
        }
    }

    /// <summary>Clears attacks, then adds new ones</summary>
    /// <param name="newAttacks">List of attack state types to add</param>
    protected void SetAttacks(List<Type> newAttacks) {
        ClearAttacks();
        AddAttacks(newAttacks);
    }

    /// <summary>Whether this enemy is attacking</summary>
    /// <returns>
    ///     True if <see cref="ActiveEnemyState"/> is an <see cref="Attack"/>.
    ///     <br/>False if not.
    /// </returns>
    protected bool IsAttacking() {
        return ActiveState.GetType().IsSubclassOf(typeof(Attack));
    }

    /// New Cooldown
    /// <summary>
    ///     Creates a new <see cref="Cooldown"/> component with the given
    ///     duration
    /// </summary>
    /// <param name="duration">Starting duration for cooldown object</param>
    /// <returns>The created cooldown component</returns>
    protected Cooldown AddCooldown(float duration = 1f) {
        Cooldown cd = gameObject.AddComponent<Cooldown>();
        cd.Duration = duration;
        return cd;
    }

    /// Spawn Add
    /// <summary>
    ///     Spawns an enemy at the given position as a sibling to this enemy's
    ///     <see cref="Transform"/>
    /// </summary>
    /// <param name="orig">Enemy to spawn</param>
    /// <param name="pos">Spawn position</param>
    /// <returns>The spawned <see cref="Enemy"/></returns>
    protected Enemy SpawnAdd(Enemy orig, Vector2 pos) {
        Enemy add = Instantiate(
            orig, pos, Quaternion.identity, transform.parent
        );
        add.Engage();
        return add;
    }

    ////////// SpawnProj Variants //////////////////////////////////////////////

    protected Projectile SpawnProj(
        Projectile.SpawnPkg sp, float? dirOverride = null, bool spawnHere = true
    ) {
        if (spawnHere) {
            sp.Position = transform.position;
            sp.Position.z = 1f;
        }
        sp.Direction = dirOverride ?? sp.Direction;

        return sp.Spawn();
    }

    protected Projectile SpawnProj(
        Projectile.SpawnPkg sp, Vector2 posOverride, float? dirOverride = null
    ) {
        sp.Position = new(posOverride.x, posOverride.y, 1);
        return SpawnProj(sp, dirOverride, false);
    }

    ////////////////////////////////////////////////////////////////////////////

    /// <summary>Called in <see cref="Awake"/>. Initializes enemy.</summary>
    void Initialize() {
        // Initialize stats
        Health = MaxHealth;
        ContactDI = new() {
            Damage = ContactDamage,
            DamageType = DmgInst.DmgType.Contact,
            Knockback = new() { Strength = KbStr, Source = transform }
        };

        // Initialize components
        NavAgent = GetComponent<NavMeshAgent>();
        NavAgent.updateRotation = false;
        NavAgent.updateUpAxis = false;
        NavAgent.speed = Speed;
        rb = GetComponent<Rigidbody2D>();

        // Get attack phase info from derived instance
        // Phase points
        FieldInfo fi = GetType().GetField(
            "PPoints", BindingFlags.Instance | BindingFlags.NonPublic
        );
        if (fi?.FieldType == typeof(List<float>)) {
            PhasePoints = (List<float>)fi.GetValue(this);
        }

        // Phases
        for (int i = 0; i < NumberOfPhases; i++) {
            // Find next phase's attack list in derived class
            fi = GetType().GetField(
                "P" + (i + 1), BindingFlags.Instance | BindingFlags.NonPublic
            );

            // Get that list if it exists, otherwise create a new empty one
            List<Type> phase = fi?.FieldType == typeof(List<Type>) ?
                (List<Type>)fi.GetValue(this) : new() { }
            ;

            // Add phase
            if (i == 0) AddAttacks(phase);
            else Phases.Add(phase);
        }

        GetPhaseShiftCheck();
        SetState<Off>();
    }

    /// Phase Shift
    /// <summary>
    ///     Called when enemy is attacked. Controls phase shifts when necessary.
    /// </summary>
    void PhaseShift() {
        if (TimeForPhaseShift()) {
            GetType().GetMethod(
                "OnPShift" + CurrentPhase,
                BindingFlags.Instance | BindingFlags.NonPublic
            )?.Invoke(this, null);
            AttackIndex = 0;
            SetAttacks(Phases[PhaseIndex++]);
            GetPhaseShiftCheck();
        }
    }

    /// <summary>Whether an attack can be performed</summary>
    /// <returns>
    ///     True if <see cref="NextReadyAttack"/> is not null.<br/>
    ///     False otherwise.
    /// </returns>
    bool AttackReady() => NextReadyAttack() != null;

    /// <summary>Angle to player</summary>
    /// <returns>
    ///     The angle in degrees of the direction from enemy to player
    /// </returns>
    public float AngleToPlayer() {
        Vector2 v2p = VectToPlayer();
        return Mathf.Atan2(v2p.y, v2p.x) * Mathf.Rad2Deg;
    }

    /// <summary>Distance to Player</summary>
    /// <returns>The distance between enemy and player</returns>
    public float DistToPlayer() => VectToPlayer().magnitude;

    /// <summary>Vector to Player</summary>
    /// <returns>The raw 2D vector from enemy to player</returns>
    public Vector2 VectToPlayer() {
        return Player.Obj.transform.position - transform.position;
    }

    /// <summary>Direction to Player</summary>
    /// <returns>The normalized 2D direction from enemy to player</returns>
    public Vector2 DirToPlayer() => VectToPlayer().normalized;

    void GetPhaseShiftCheck() {
        if (CurrentPhase < NumberOfPhases) {
            MethodInfo mi = GetType().GetMethod(
                "PCheck" + CurrentPhase,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            PhaseShiftCheck = mi?.ReturnType == typeof(bool) ? mi : null;
        } else PhaseShiftCheck = null;
    }

    bool TimeForPhaseShift() {
        if (CurrentPhase < NumberOfPhases) {
            return PhaseShiftCheck == null ?
                HealthBasedPhaseShift() :
                (bool)PhaseShiftCheck.Invoke(this, null)
            ;
        }
        return false;
    }

    bool HealthBasedPhaseShift() {
        return (float)Health / MaxHealth <= PhasePoints[PhaseIndex];
    }

    /// <summary>Gets next attack that can be performed</summary>
    /// <returns>
    ///     Next ready attack type.<br/>
    ///     Null if there are none.
    /// </returns>
    Type NextReadyAttack() {
        for (int i = 0; i < Attacks.Count; i++) {
            Type nextAttack = Attacks[AttackIndex++];
            if (GetAttackState(nextAttack).CanPerform()) return nextAttack;
        }
        return null;
    }

    /// <summary>Gets attack state object from type</summary>
    /// <param name="attackType">Type of attack state to return</param>
    /// <returns>State object of type <paramref name="attackType"/></returns>
    Attack GetAttackState(Type attackType) => (Attack)GetState(attackType);

    /// On Awake
    /// <summary>
    ///     Called at end of <see cref="Awake"/>. Empty by default. Override for
    ///     derived initialization.
    /// </summary>
    protected virtual void OnAwake() { }

    /// On Hit
    /// <summary>
    ///     Called when a hit is registered if this enemy is not invincible and
    ///     <see cref="Health"/> is greater than 0. Damages enemy and attempts
    ///     phase shift by default.
    /// </summary>
    /// <param name="di">Damage instance to use for hit</param>
    protected virtual void OnHit(DmgInst di) => Health -= (int)di.Damage;

    /// On Zero Health
    /// <summary>
    ///     Called after <see cref="OnHit"/> when a hit is registered if this
    ///     enemy is not invincible and <see cref="Health"/> is less than or
    ///     equal to 0. Sets <see cref="Health"/> to 0 and <see cref="Alive"/>
    ///     to false by default.
    /// </summary>
    protected virtual void OnZeroHealth() => Alive = false;

    /// Priority State Transitions
    /// <summary>
    ///     Called by <see cref="EnemyState.Transitions"/>, which will fall back
    ///     to state-specific transitions if null is returned
    /// </summary>
    /// <returns>
    ///     By default, returns typeof <see cref="Death"/> if
    ///     <see cref="Alive"/> is false or null otherwise
    /// </returns>
    protected virtual Type PriorityStateTransitions() {
        return Alive ? null : typeof(Death);
    }

    ////////// Overridable Behaviors for Built-In States ///////////////////////
    ////////// Off State /////////////////////////////////////////////

    /// Off State Initialization Addendum
    /// <summary>
    ///     Initialization for <see cref="Off"/> state<br/>
    ///     Empty by default<br/>
    ///     Runs after built-in Off state initialization
    /// </summary>
    protected virtual void Init_Off() { }

    /// Off State Enter
    /// <summary>
    ///     Enter function for <see cref="Off"/> state<br/>
    ///     Empty by default
    /// </summary>
    protected virtual void Enter_Off() { }

    /// Off State Update
    /// <summary>
    ///     Update function for <see cref="Off"/> state<br/>
    ///     Empty by default
    /// </summary>
    protected virtual void Update_Off() { }

    /// Off State Exit
    /// <summary>
    ///     Exit function for <see cref="Off"/> state<br/>
    ///     Empty by default
    /// </summary>
    protected virtual void Exit_Off() { }

    /// <summary>Transitions for <see cref="Off"/> state</summary>
    /// <returns>
    ///     By default, returns <see cref="TDef_Off2Main"/> (which is typeof
    ///     <see cref="Main"/> by default) if <see cref="Idle"/> is false or
    ///     null otherwise
    /// </returns>
    protected virtual Type Trans_Off() => Idle ? null : TDef_Off2Main;

    //////////////////////////////////////////////////////////////////
    ////////// Main State ////////////////////////////////////////////

    /// Main State Initialization
    /// <summary>
    ///     Initialization for <see cref="Main"/> state<br/>
    ///     Empty by default
    /// </summary>
    protected virtual void Init_Main() { }

    /// Main State Enter Addendum
    /// <summary>
    ///     Enter function for <see cref="Main"/> state<br/>
    ///     Empty by default<br/>
    ///     Runs after built-in Main state enter procedures
    /// </summary>
    protected virtual void Enter_Main() { }

    /// Main State Update
    /// <summary>
    ///     Update function for <see cref="Main"/> state<br/>
    ///     Empty by default
    /// </summary>
    protected virtual void Update_Main() { }

    /// Main State Exit Addendum
    /// <summary>
    ///     Exit function for <see cref="Main"/> state<br/>
    ///     Empty by default<br/>
    ///     Runs after built-in Main state exit procedures
    /// </summary>
    protected virtual void Exit_Main() { }

    protected virtual Type Trans_Main() {
        Type next = NextReadyAttack();
        if (next != null) return next;
        return null;
    }

    //////////////////////////////////////////////////////////////////
    ////////// Death State ///////////////////////////////////////////

    /// Death State Enter Addendum
    /// <summary>
    ///     Enter function for <see cref="Death"/> state<br/>
    ///     Empty by default<br/>
    ///     Runs before enemy is destroyed during built-in Death state enter
    ///     procedures
    /// </summary>
    protected virtual void Enter_Death() { }

    ////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////
}
