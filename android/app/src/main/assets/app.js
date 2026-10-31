(function() {
    'use strict';

    // State
    const state = {
        mode: 'f1',
        connected: false,
        f1Active: false,
        steerAngleDeg: 0,
        steerZeroOffset: 0,
        lockAngle: 360,
        deadzone: 0.02,
        linearity: 1.0,
        hapticEnabled: true,
        throttle: 0.0,
        brake: 0.0,
        clutch: 0.0,
        buttonsMask: 0,
        seq: 0,
        lastGear: 0
    };

    // ViGEm Button Bits
    const BTN = {
        DPAD_UP: 0x0001,
        DPAD_DOWN: 0x0002,
        DPAD_LEFT: 0x0004,
        DPAD_RIGHT: 0x0008,
        START: 0x0010,
        BACK: 0x0020,
        LEFT_THUMB: 0x0040,
        RIGHT_THUMB: 0x0080,
        LB: 0x0100, // Downshift
        RB: 0x0200, // Upshift
        GUIDE: 0x0400,
        A: 0x1000,
        B: 0x2000,
        X: 0x4000,
        Y: 0x8000  // DRS
    };

    // DOM Elements
    const elements = {
        connBadge: document.getElementById('conn-badge'),
        telemetryBadge: document.getElementById('telemetry-badge'),
        calibrateBtn: document.getElementById('calibrate-btn'),
        fullscreenBtn: document.getElementById('fullscreen-btn'),
        modeBtns: document.querySelectorAll('.mode-btn'),
        views: {
            f1: document.getElementById('f1-wheel-view'),
            gt: document.getElementById('gt-wheel-view'),
            gamepad: document.getElementById('gamepad-view')
        },
        // F1 HUD
        revLights: document.querySelectorAll('.led'),
        revBar: document.getElementById('rev-lights'),
        valSpeed: document.getElementById('val-speed'),
        valGear: document.getElementById('val-gear'),
        valRpm: document.getElementById('val-rpm'),
        valMaxRpm: document.getElementById('val-maxrpm'),
        drsIndicator: document.getElementById('drs-indicator'),
        ersNum: document.getElementById('ers-num'),
        ersBar: document.getElementById('ers-bar'),
        flagIndicator: document.getElementById('flag-indicator'),
        flagText: document.getElementById('flag-text'),
        tempFL: document.getElementById('temp-fl'),
        tempFR: document.getElementById('temp-fr'),
        tempRL: document.getElementById('temp-rl'),
        tempRR: document.getElementById('temp-rr'),
        steerDegrees: document.getElementById('steer-degrees'),
        valLap: document.getElementById('val-lap'),
        valPos: document.getElementById('val-pos'),
        // Pedals
        touchBrake: document.getElementById('touch-brake'),
        brakeFill: document.getElementById('brake-fill'),
        touchThrottle: document.getElementById('touch-throttle'),
        throttleFill: document.getElementById('throttle-fill'),
        // Paddles & Buttons
        paddleDown: document.getElementById('paddle-down'),
        paddleUp: document.getElementById('paddle-up'),
        btnDrs: document.getElementById('btn-drs'),
        btnErs: document.getElementById('btn-ers'),
        btnPit: document.getElementById('btn-pit'),
        // Settings Modal
        settingsModal: document.getElementById('settings-modal'),
        modalCloseBtn: document.getElementById('modal-close-btn'),
        lockAngleSelect: document.getElementById('lock-angle-select'),
        deadzoneSlider: document.getElementById('deadzone-slider'),
        deadzoneVal: document.getElementById('deadzone-val'),
        linearitySlider: document.getElementById('linearity-slider'),
        linearityVal: document.getElementById('linearity-val')
    };

    // 1. Gyroscope / Device Orientation Steering
    function initGyroscope() {
        if (window.DeviceOrientationEvent) {
            // iOS 13+ permission request
            if (typeof DeviceOrientationEvent.requestPermission === 'function') {
                document.body.addEventListener('click', function requestPerm() {
                    DeviceOrientationEvent.requestPermission().then(function(res) {
                        if (res === 'granted') {
                            window.addEventListener('deviceorientation', handleOrientation);
                        }
                    }).catch(console.error);
                    document.body.removeEventListener('click', requestPerm);
                });
            } else {
                window.addEventListener('deviceorientation', handleOrientation);
            }
        }
    }

    function handleOrientation(e) {
        // When phone is held in landscape mode:
        // gamma is rotation around the long axis (turning the wheel left/right)
        // or beta depending on orientation angle.
        let rawAngle = 0;
        const orientation = window.orientation || (screen.orientation ? screen.orientation.angle : 0);

        if (orientation === 90) {
            rawAngle = -e.beta;
        } else if (orientation === -90 || orientation === 270) {
            rawAngle = e.beta;
        } else {
            rawAngle = e.gamma || 0;
        }

        // Apply zero tare
        let calibratedAngle = rawAngle - state.steerZeroOffset;
        state.steerAngleDeg = calibratedAngle;

        if (elements.steerDegrees) {
            elements.steerDegrees.textContent = Math.round(calibratedAngle) + '°';
        }

        // Send to native bridge if present
        if (window.ApexNative && window.ApexNative.onSteerAngle) {
            window.ApexNative.onSteerAngle(calibratedAngle);
        }
    }

    // 2. Tactile Touch Pedals (Progressive Travel)
    function setupPedal(elem, fillElem, onValueChange) {
        if (!elem) return;

        function updatePedalFromTouch(touch) {
            const rect = elem.getBoundingClientRect();
            const y = touch.clientY - rect.top;
            // Inverted: bottom = 100%, top = 0% or vice versa depending on hold
            let ratio = 1.0 - (y / rect.height);
            ratio = Math.max(0.0, Math.Min ? Math.min(1.0, ratio) : Math.min(1.0, ratio));
            fillElem.style.height = (ratio * 100).toFixed(1) + '%';
            onValueChange(ratio);
        }

        elem.addEventListener('touchstart', function(e) {
            e.preventDefault();
            updatePedalFromTouch(e.touches[0]);
        }, { passive: false });

        elem.addEventListener('touchmove', function(e) {
            e.preventDefault();
            updatePedalFromTouch(e.touches[0]);
        }, { passive: false });

        elem.addEventListener('touchend', function(e) {
            e.preventDefault();
            fillElem.style.height = '0%';
            onValueChange(0.0);
        }, { passive: false });

        // Mouse fallback for testing on laptop
        let isMouseDown = false;
        elem.addEventListener('mousedown', function(e) {
            isMouseDown = true;
            updatePedalFromTouch(e);
        });
        window.addEventListener('mousemove', function(e) {
            if (isMouseDown) updatePedalFromTouch(e);
        });
        window.addEventListener('mouseup', function() {
            if (isMouseDown) {
                isMouseDown = false;
                fillElem.style.height = '0%';
                onValueChange(0.0);
            }
        });
    }

    // 3. Button & Paddle Bindings
    function bindButton(elem, bit, hapticMs) {
        if (!elem) return;

        function press(e) {
            if (e) e.preventDefault();
            elem.classList.add('active');
            state.buttonsMask |= bit;
            triggerHaptic(hapticMs || 25);
        }

        function release(e) {
            if (e) e.preventDefault();
            elem.classList.remove('active');
            state.buttonsMask &= ~bit;
        }

        elem.addEventListener('touchstart', press, { passive: false });
        elem.addEventListener('touchend', release, { passive: false });
        elem.addEventListener('mousedown', press);
        elem.addEventListener('mouseup', release);
        elem.addEventListener('mouseleave', release);
    }

    function triggerHaptic(durationMs) {
        if (!state.hapticEnabled) return;
        if (window.ApexNative && window.ApexNative.vibrate) {
            window.ApexNative.vibrate(durationMs);
        } else if (navigator.vibrate) {
            navigator.vibrate(durationMs);
        }
    }

    // 4. Fast Input Transmission Loop (60-100Hz)
    function startInputLoop() {
        setInterval(function() {
            state.seq++;
            const payload = 'APEX:' + state.seq + ':' +
                state.steerAngleDeg.toFixed(2) + ':' +
                state.throttle.toFixed(3) + ':' +
                state.brake.toFixed(3) + ':' +
                state.clutch.toFixed(3) + ':' +
                state.buttonsMask;

            // Send via HTTP POST
            fetch('/api/input', {
                method: 'POST',
                headers: { 'Content-Type': 'text/plain' },
                body: payload
            }).catch(function() {
                // Ignore transient network errors
            });
        }, 16); // ~60fps
    }

    // 5. Telemetry Polling Loop (Update F1 Cockpit HUD)
    function startTelemetryLoop() {
        setInterval(function() {
            fetch('/api/telemetry')
                .then(function(r) { return r.json(); })
                .then(function(data) {
                    updateF1Hud(data);
                })
                .catch(function() {
                    setConnected(false);
                });

            fetch('/api/status')
                .then(function(r) { return r.json(); })
                .then(function(status) {
                    setConnected(true);
                    if (elements.telemetryBadge) {
                        if (status.f1Active) {
                            elements.telemetryBadge.className = 'badge f1-active';
                            elements.telemetryBadge.textContent = 'F1 25 LIVE';
                        } else {
                            elements.telemetryBadge.className = 'badge f1-idle';
                            elements.telemetryBadge.textContent = 'F1 25 Idle';
                        }
                    }
                })
                .catch(function() {
                    setConnected(false);
                });
        }, 100);
    }

    function setConnected(conn) {
        state.connected = conn;
        if (elements.connBadge) {
            if (conn) {
                elements.connBadge.className = 'badge connected';
                elements.connBadge.textContent = 'Connected';
            } else {
                elements.connBadge.className = 'badge disconnected';
                elements.connBadge.textContent = 'Disconnected';
            }
        }
    }

    function updateF1Hud(d) {
        if (!d) return;

        // Speed
        if (elements.valSpeed) elements.valSpeed.textContent = d.speed || 0;

        // Gear
        if (elements.valGear) {
            let gearText = 'N';
            if (d.gear === -1) gearText = 'R';
            else if (d.gear === 0) gearText = 'N';
            else if (d.gear > 0) gearText = d.gear.toString();
            elements.valGear.textContent = gearText;

            // Haptic on gear shift
            if (d.gear !== state.lastGear) {
                triggerHaptic(40);
                state.lastGear = d.gear;
            }
        }

        // RPM & Rev Lights
        if (elements.valRpm) elements.valRpm.textContent = d.rpm || 0;
        if (elements.valMaxRpm && d.maxRpm) elements.valMaxRpm.textContent = '/' + d.maxRpm;

        // 15 Rev LEDs calculation
        const revPercent = d.revPercent || 0;
        const litCount = Math.floor((revPercent / 100) * 15);
        if (elements.revLights) {
            elements.revLights.forEach(function(led, idx) {
                if (idx < litCount) {
                    led.classList.add('lit');
                } else {
                    led.classList.remove('lit');
                }
            });
        }

        // Redline limiter strobe
        if (revPercent >= 96) {
            elements.revBar.classList.add('redline');
            triggerHaptic(15);
        } else {
            elements.revBar.classList.remove('redline');
        }

        // DRS
        if (elements.drsIndicator) {
            if (d.drsActive) {
                elements.drsIndicator.className = 'drs-status-box active';
            } else if (d.drsAllowed) {
                elements.drsIndicator.className = 'drs-status-box ready';
            } else {
                elements.drsIndicator.className = 'drs-status-box';
            }
        }

        // ERS
        if (elements.ersNum) elements.ersNum.textContent = Math.round(d.ers || 100) + '%';
        if (elements.ersBar) elements.ersBar.style.width = (d.ers || 100) + '%';

        // Tyres
        if (d.tyres && d.tyres.length >= 4) {
            setTyreBox(elements.tempRL, d.tyres[0]);
            setTyreBox(elements.tempRR, d.tyres[1]);
            setTyreBox(elements.tempFL, d.tyres[2]);
            setTyreBox(elements.tempFR, d.tyres[3]);
        }

        // Lap & Position
        if (elements.valPos && d.position) elements.valPos.textContent = 'P' + d.position;
        if (elements.valLap && d.lapTime) {
            const m = Math.floor(d.lapTime / 60);
            const s = (d.lapTime % 60).toFixed(2);
            elements.valLap.textContent = m + ':' + (s < 10 ? '0' : '') + s;
        }
    }

    function setTyreBox(elem, temp) {
        if (!elem) return;
        elem.textContent = temp + '°';
        const parent = elem.closest('.tyre-box');
        if (!parent) return;
        if (temp > 105) {
            parent.className = 'tyre-box hot';
        } else if (temp >= 80) {
            parent.className = 'tyre-box optimal';
        } else {
            parent.className = 'tyre-box';
        }
    }

    // 6. UI Handlers & Initialization
    function init() {
        initGyroscope();

        // Setup touch pedals
        setupPedal(elements.touchBrake, elements.brakeFill, function(v) { state.brake = v; });
        setupPedal(elements.touchThrottle, elements.throttleFill, function(v) { state.throttle = v; });

        // Bind F1 buttons
        bindButton(elements.paddleDown, BTN.LB, 35); // Downshift
        bindButton(elements.paddleUp, BTN.RB, 35);   // Upshift
        bindButton(elements.btnDrs, BTN.Y, 25);      // DRS
        bindButton(elements.btnErs, BTN.LEFT_THUMB, 25); // ERS / Overtake
        bindButton(elements.btnPit, BTN.DPAD_DOWN, 25);  // Pit Limiter

        // Mode Switching
        elements.modeBtns.forEach(function(btn) {
            btn.addEventListener('click', function() {
                elements.modeBtns.forEach(function(b) { b.classList.remove('active'); });
                btn.classList.add('active');
                const mode = btn.getAttribute('data-mode');
                state.mode = mode;
                Object.keys(elements.views).forEach(function(k) {
                    if (k === mode) elements.views[k].classList.add('active');
                    else elements.views[k].classList.remove('active');
                });
            });
        });

        // Tare / Calibrate Button
        if (elements.calibrateBtn) {
            elements.calibrateBtn.addEventListener('click', function() {
                state.steerZeroOffset += state.steerAngleDeg;
                state.steerAngleDeg = 0;
                triggerHaptic(50);
            });
        }

        // Fullscreen
        if (elements.fullscreenBtn) {
            elements.fullscreenBtn.addEventListener('click', function() {
                if (!document.fullscreenElement) {
                    document.documentElement.requestFullscreen().catch(console.error);
                } else {
                    document.exitFullscreen().catch(console.error);
                }
            });
        }

        startInputLoop();
        startTelemetryLoop();
    }

    document.addEventListener('DOMContentLoaded', init);
})();
