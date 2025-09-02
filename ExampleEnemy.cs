using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ExampleEnemy : Enemy {
    List<float> PPoints = new() { 0.5f };
    List<Type> P1 = new() { typeof(Dash) };
    List<Type> P2 = new() { typeof(Dash), typeof(Shoot) };

    [SerializeField] AnimationCurve WindUpSpeedCurve, DashSpeedCurve;
    [SerializeField] Projectile.SpawnPkg Proj;

    class Dash : Attack<ExampleEnemy> {
        protected override void PreInit() {
            WaitTime = 3f;
        }

        protected override IEnumerator Script() {
            // Move slowly away from player for a moment
            yield return AnimMove<WindUp>();

            // Dash toward player's current position
            yield return AnimMove(DirToPlayer(), 1f, Up.DashSpeedCurve);

            // Sit still for a moment
            yield return AnimMove(Vector2.zero, 0.25f);
        }

        class WindUp : MoveAnimation<Dash> {
            public override void Setup() {
                Duration = 2f;
                SpeedCurve = Up.Up.WindUpSpeedCurve;
            }

            protected override Vector2 CurrentVel() {
                return -Up.DirToPlayer();
            }
        }
    }

    class Shoot : Attack<ExampleEnemy> {
        protected override void PreInit() {
            WaitTime = 0.1f;
        }

        protected override IEnumerator Script() {
            SpawnProj(Up.Proj, AngleToPlayer());
            yield break;
        }
    }
}
