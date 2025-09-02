using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// BehaviourFSM
/// <summary>
///     <see cref="MonoBehaviour"/> class that acts as a finite state machine
/// </summary>
public class BehaviourFSM : MonoBehaviour {
    /// <summary>The currently active state</summary>
    protected State_Base ActiveState { get; private set; } = null;

    /// <summary>List of already-created states</summary>
    Dictionary<Type, State_Base> States = new();

    void Update() {
        OnUpdate();
        ActiveState?.Update();
        SetState(ActiveState?.Transitions());
    }

    /// Preload State
    /// <summary>
    ///     Adds a state to the machine, if it is not already there, without
    ///     switching to it.
    /// </summary>
    /// <typeparam name="T">The type of the state to add</typeparam>
    protected void LoadState<T>() where T : State_Base, new() {
        AddState<T>();
    }

    /// Preload State (Non-generic)
    /// <summary>
    ///     Adds a state to the machine, if it is not already there, without
    ///     switching to it.
    /// </summary>
    /// <param name="stateType">The type of the state to add</param>
    protected void LoadState(Type stateType) {
        if (stateType != null) AddState(stateType);
    }

    /// <summary>Gets a state object from the machine</summary>
    /// <typeparam name="T">The type of the state to get</typeparam>
    /// <returns>
    ///     The state of type <typeparamref name="T"/><br/>
    ///     Returns null if state does not exist
    /// </returns>
    protected T GetState<T>() {
        return (T)(object)GetState(typeof(T));
    }

    /// <summary>Gets a state object from the machine</summary>
    /// <param name="stateType">The type of the state to get</param>
    /// <returns>
    ///     The state of type <paramref name="stateType"/><br/>
    ///     Returns null if state does not exist
    /// </returns>
    protected State_Base GetState(Type stateType) {
        if (States.ContainsKey(stateType)) return States[stateType];
        return null;
    }

    /// <summary>Sets the active state</summary>
    /// <typeparam name="T">The type of the state to set</typeparam>
    protected void SetState<T>() where T : State_Base, new() {
        ActiveState?.Exit();
        ActiveState = AddState<T>();
        ActiveState.Enter();
    }

    /// <summary>Sets the active state</summary>
    /// <param name="stateType">The type of the state to set</param>
    protected void SetState(Type stateType) {
        if (stateType != null) {
            ActiveState?.Exit();
            ActiveState = AddState(stateType);
            ActiveState.Enter();
        }
    }
    /// <summary>Removes a state from the machine</summary>
    /// <typeparam name="T">The type of the state to remove</typeparam>
    protected void RemoveState<T>() {
        RemoveState(typeof(T));
    }

    /// <summary>Removes a state from the machine</summary>
    /// <param name="stateType">The type of the state to remove</param>
    protected void RemoveState(Type stateType) {
        if (States.ContainsKey(stateType)) States.Remove(stateType);
    }

    /// <summary>Adds a state to the machine</summary>
    /// <typeparam name="T">The type of the state to add</typeparam>
    /// <returns>The added state</returns>
    T AddState<T>() where T : State_Base, new() {
        Type stateType = typeof(T);
        T state;
        if (!States.ContainsKey(stateType)) {
            state = new T();
            States.Add(stateType, state);
            state.MachineBase = this;
            state.Init();
        } else state = (T)States[stateType];
        return state;
    }

    /// <summary>Adds a state to the machine</summary>
    /// <param name="stateType">The type of the state to add</param>
    /// <returns>
    ///     The added state<br/>
    ///     Returns null if <paramref name="stateType"/> is null
    /// </returns>
    State_Base AddState(Type stateType) {
        if (stateType != null) {
            State_Base state;
            if (!States.ContainsKey(stateType)) {
                state = (State_Base)Activator.CreateInstance(stateType);
                States.Add(stateType, state);
                state.MachineBase = this;
                state.Init();
            } else state = States[stateType];
            return state;
        }
        return null;
    }

    /// On Update
    /// <summary>
    ///     Called before the current state's update method each frame. Used as
    ///     a replacement for <see cref="Update"/> in derived machines<br/>
    ///     Empty by default
    /// </summary>
    protected virtual void OnUpdate() { }

    /// <summary>Base class for all state types</summary>
    public abstract class State_Base {
        /// <summary>Pointer to parent machine's derivation</summary>
        /// <value>To be set by parent machine on state creation</value>
        public abstract BehaviourFSM MachineBase { protected get; set; }

        /// Init
        /// <summary>
        ///     Called by the parent machine when this state is created. Empty
        ///     by default
        /// </summary>
        public virtual void Init() { }

        /// Enter
        /// <summary>
        ///     Called by the machine when transitioning into this state. Empty
        ///     by default
        /// </summary>
        public virtual void Enter() { }

        /// Update
        /// <summary>
        ///     Called by the machine every frame while this state is active.
        ///     Empty by default
        /// </summary>
        public virtual void Update() { }

        /// Exit
        /// <summary>
        ///     Called by the machine when transitioning out of this state.
        ///     Empty by default
        /// </summary>
        public virtual void Exit() { }

        /// Transition Rules
        /// <summary>
        ///     Rules for state transitions. Checked by the machine after Update
        ///     is called each frame. The state will change to the returned type
        ///     unless it is null
        /// </summary>
        /// <returns>
        ///     The type of the state to switch to. Always returns null by
        ///     default
        /// </returns>
        public virtual Type Transitions() { return null; }
    }

    /// <summary>Default derivable state type</summary>
    public class State : State_Base {
        BehaviourFSM _MachineBase;
        /// <summary>Pointer to the parent machine's base class</summary>
        /// <value>
        ///     Set only if null.<br/>
        ///     To be set by parent machine on state creation
        /// </value>
        public sealed override BehaviourFSM MachineBase {
            protected get => _MachineBase;
            set => _MachineBase ??= value;
        }
    }

    /// <summary>Generic state with pointer to derived parent machine</summary>
    /// <typeparam name="T">Parent machine derived type</typeparam>
    public class State<T> : State_Base where T : BehaviourFSM {
        T _Machine;
        /// <summary>Pointer to the parent machine's derived class</summary>
        /// <value>
        ///     Set only if null<br/>
        ///     To be set by <see cref="MachineBase"/> on state creation
        /// </value>
        protected T Machine {
            get => _Machine;
            set => _Machine ??= value;
        }

        BehaviourFSM _MachineBase;
        /// <summary>Pointer to the parent machine's base class</summary>
        /// <value>
        ///     Sets self and <see cref="Machine"/> only if null<br/>
        ///     To be set by parent machine on state creation
        /// </value>
        public sealed override BehaviourFSM MachineBase {
            protected get => _MachineBase;
            set {
                if (_MachineBase == null) {
                    _MachineBase = value;
                    Machine = (T)_MachineBase;
                }
            }
        }
    }
}
