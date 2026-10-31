(function() {
    'use strict';

    // State
    const state = {
        mode: 'racepad', // 'racepad' or 'gamepad'
        connected: false,
        f1Active: false,
        steerAngleDeg: 0,
        steerZeroOffset: 0,
        lockAngle: 360,
        deadzone: 0.02,
        throttle: 0.0,
        brake: 0.0,
        clutch: 0.0,
        buttonsMask: 0,
        seq: 0,
        lastGear: 0
    };

    // ViGEm Xbox 360 Controller Button Bitmask
    const BTN = {
        DPAD_UP:        0x0001,
        DPAD_DOWN:      0x0002,
        DPAD_LEFT:      0x0004,
        DPAD_RIGHT:     0x0008,
        START:          0x0010,
        BACK:           0x0020,
        LEFT_THUMB:     0x0040, // ERS / OT
        RIGHT_THUMB:    0x0080,
        LEFT_SHOULDER:  0x0100, // Downshift Paddle
        RIGHT_SHOULDER: 0x0200, // Upshift Paddle
        GUIDE:          0x0400,
        A:              0x1000,
        B:              0x2000, // ERS secondary
        X:              0x4000,
        Y:              0x8000  // DRS
    };

    // DOM Elements
    const elements = {
        topStatus: document.getElementById('gp-top-status'),
        statusText: document.getElementById('gp-status-text'),
        btnCalibrate: document.getElementById('btn-calibrate'),
        btnFullscreen: document.getElementById('btn-fullscreen'),
        viewRacepad: document.getElementById('view-racepad'),
        viewGamepad: document.getElementById('view-gamepad'),
        modeToggle: document.getElementById('gp-mode-toggle'),
        gamepadToggle: document.getElementById('gp-gamepad-toggle'),
        // Telemetry Displays
        pillGear: document.getElementById('pill-gear-disp'),
        pillSpeed: document.getElementById('pill-speed-disp'),
        pillDrs: document.getElementById('pill-drs-badge'),
        arcGear: document.getElementById('arc-gear'),
        arcSpeed: document.getElementById('arc-speed'),
        arcDeg: document.getElementById('arc-deg'),
        arcNeedle: document.getElementById('arc-needle'),
        revBar: document.getElementById('f1-rev-bar'),
        revLeds: document.querySelectorAll('.r-led'),
        // Pedals
        pedalBrake: document.getElementById('pedal-brake'),
        fillBrake: document.getElementById('fill-brake'),
        pedalThrottle: document.getElementById('pedal-throttle'),
        fillThrottle: document.getElementById('fill-throttle'),
        pedalClutch: document.getElementById('pedal-clutch'),
        fillClutch: document.getElementById('fill-clutch'),
        // Shifters
        paddleDown: document.getElementById('paddle-down'),
        paddleUp: document.getElementById('paddle-up'),
        // Function Buttons
        btnErs: document.getElementById('btn-ers'),
        btnPit: document.getElementById('btn-pit'),
        btnRadio: document.getElementById('btn-radio'),
        // D-Pad
        btnDup: document.getElementById('btn-dup'),
        btnDdown: document.getElementById('btn-ddown'),
        btnDleft: document.getElementById('btn-dleft'),
        btnDright: document.getElementById('btn-dright'),
        // Face Buttons
        btnY: document.getElementById('btn-y'),
        btnX: document.getElementById('btn-x'),
        btnA: document.getElementById('btn-a'),
        btnB: document.getElementById('btn-b')
    };

    // 1. Gyroscope / Device Orientation
    function initGyroscope() {
        if (window.DeviceOrientationEvent) {
            if (typeof DeviceOrientationEvent.requestPermission === 'function') {
                document.body.addEventListener('click', function req() {
                    DeviceOrientationEvent.requestPermission().then(function(res) {
                        if (res === 'granted') {
                            window.addEventListener('deviceorientation', handleOrientation);
                        }
                    }).catch(console.error);
                    document.body.removeEventListener('click', req);
                });
            } else {
                window.addEventListener('deviceorientation', handleOrientation);
            }
        }
    }

    function handleOrientation(e) {
        let rawAngle = 0;
        const orient = window.orientation || (screen.orientation ? screen.orientation.angle : 0);

        if (orient === 90) rawAngle = -e.beta;
        else if (orient === -90 || orient === 270) rawAngle = e.beta;
        else rawAngle = e.gamma || 0;

        let calAngle = rawAngle - state.steerZeroOffset;
        state.steerAngleDeg = calAngle;

        // Rotate SVG needle on the Steering Arc
        if (elements.arcNeedle) {
            let clamped = Math.max(-90, Math.min(90, (calAngle / (state.lockAngle / 2)) * 90));
            elements.arcNeedle.setAttribute('transform', 'rotate(' + clamped.toFixed(1) + ' 150 140)');
        }
        if (elements.arcDeg) {
            elements.arcDeg.textContent = Math.round(calAngle) + '°';
        }

        if (window.ApexNative && window.ApexNative.onSteerAngle) {
            window.ApexNative.onSteerAngle(calAngle);
        }
    }

    // 2. Touch Pedals (Progressive Travel)
    function setupPedal(elem, fillElem, onChange) {
        if (!elem) return;

        function updateFromTouch(touch) {
            const rect = elem.getBoundingClientRect();
            const y = touch.clientY - rect.top;
            let val = 1.0 - (y / rect.height);
            val = Math.max(0.0, Math.min(1.0, val));
            fillElem.style.height = (val * 100).toFixed(1) + '%';
            onChange(val);
        }

        elem.addEventListener('touchstart', function(e) {
            e.preventDefault();
            updateFromTouch(e.touches[0]);
        }, { passive: false });

        elem.addEventListener('touchmove', function(e) {
            e.preventDefault();
            updateFromTouch(e.touches[0]);
        }, { passive: false });

        elem.addEventListener('touchend', function(e) {
            e.preventDefault();
            fillElem.style.height = '0%';
            onChange(0.0);
        }, { passive: false });

        // Mouse fallback
        let isDown = false;
        elem.addEventListener('mousedown', function(e) {
            isDown = true;
            updateFromTouch(e);
        });
        window.addEventListener('mousemove', function(e) {
            if (isDown) updateFromTouch(e);
        });
        window.addEventListener('mouseup', function() {
            if (isDown) {
                isDown = false;
                fillElem.style.height = '0%';
                onChange(0.0);
            }
        });
    }

    // 3. Button Bindings
    function bindButton(elem, bit, hapticMs) {
        if (!elem) return;

        function press(e) {
            if (e) e.preventDefault();
            elem.classList.add('active');
            state.buttonsMask |= bit;
            vibrate(hapticMs || 25);
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

    function vibrate(ms) {
        if (window.ApexNative && window.ApexNative.vibrate) {
            window.ApexNative.vibrate(ms);
        } else if (navigator.vibrate) {
            navigator.vibrate(ms);
        }
    }

    // 4. High-Speed Input Transmission (100Hz)
    function startInputLoop() {
        setInterval(function() {
            state.seq++;
            const payload = 'APEX:' + state.seq + ':' +
                state.steerAngleDeg.toFixed(2) + ':' +
                state.throttle.toFixed(3) + ':' +
                state.brake.toFixed(3) + ':' +
                state.clutch.toFixed(3) + ':' +
                state.buttonsMask;

            fetch('/api/input', {
                method: 'POST',
                headers: { 'Content-Type': 'text/plain' },
                body: payload
            }).catch(function() {});
        }, 12);
    }

    // 5. Telemetry Polling (F1 25 Live Telemetry)
    function startTelemetryLoop() {
        setInterval(function() {
            fetch('/api/telemetry')
                .then(function(r) { return r.json(); })
                .then(function(data) {
                    updateTelemetryUI(data);
                })
                .catch(function() {
                    setConnected(false);
                });

            fetch('/api/status')
                .then(function(r) { return r.json(); })
                .then(function(s) {
                    setConnected(true);
                    if (s.f1Active) {
                        elements.topStatus.className = 'gp-center-status live';
                        elements.statusText.textContent = '● F1 25 LIVE TELEMETRY';
                    } else {
                        elements.topStatus.className = 'gp-center-status';
                        elements.statusText.textContent = '○ F1 25 WAITING (PORT 20777)';
                    }
                })
                .catch(function() {
                    setConnected(false);
                });
        }, 80);
    }

    function setConnected(conn) {
        state.connected = conn;
    }

    function updateTelemetryUI(d) {
        if (!d) return;

        // Gear display
        let gearStr = 'N';
        if (d.gear === -1) gearStr = 'R';
        else if (d.gear > 0) gearStr = d.gear.toString();

        if (elements.pillGear) elements.pillGear.textContent = gearStr;
        if (elements.arcGear) elements.arcGear.textContent = gearStr;

        if (d.gear !== state.lastGear) {
            vibrate(40);
            state.lastGear = d.gear;
        }

        // Speed
        let speedStr = (d.speed || 0) + ' KM/H';
        if (elements.pillSpeed) elements.pillSpeed.textContent = speedStr;
        if (elements.arcSpeed) elements.arcSpeed.innerHTML = (d.speed || 0) + ' <small>KM/H</small>';

        // 15 Shift LEDs
        const revPercent = d.revPercent || 0;
        const litCount = Math.floor((revPercent / 100) * 15);
        if (elements.revLeds) {
            elements.revLeds.forEach(function(led, idx) {
                if (idx < litCount) led.classList.add('lit');
                else led.classList.remove('lit');
            });
        }

        if (revPercent >= 96) {
            elements.revBar.classList.add('redline');
            vibrate(15);
        } else {
            elements.revBar.classList.remove('redline');
        }

        // DRS
        if (elements.pillDrs) {
            if (d.drsActive) elements.pillDrs.className = 'drs-badge active';
            else if (d.drsAllowed) elements.pillDrs.className = 'drs-badge ready';
            else elements.pillDrs.className = 'drs-badge';
        }
    }

    // 6. Mode Switcher
    function toggleMode() {
        if (state.mode === 'racepad') {
            state.mode = 'gamepad';
            elements.viewRacepad.classList.remove('active');
            elements.viewGamepad.classList.add('active');
        } else {
            state.mode = 'racepad';
            elements.viewGamepad.classList.remove('active');
            elements.viewRacepad.classList.add('active');
        }
    }

    // 7. Initialization
    function init() {
        initGyroscope();

        // Setup touch pedals
        setupPedal(elements.pedalBrake, elements.fillBrake, function(v) { state.brake = v; });
        setupPedal(elements.pedalThrottle, elements.fillThrottle, function(v) { state.throttle = v; });
        setupPedal(elements.pedalClutch, elements.fillClutch, function(v) { state.clutch = v; });

        // Bind Paddles
        bindButton(elements.paddleDown, BTN.LEFT_SHOULDER, 35); // Gear Down
        bindButton(elements.paddleUp, BTN.RIGHT_SHOULDER, 35);  // Gear Up

        // Bind F1 Controls
        bindButton(elements.btnErs, BTN.LEFT_THUMB, 25);        // ERS / OT
        bindButton(elements.btnPit, BTN.DPAD_DOWN, 25);         // Pit Limiter
        bindButton(elements.btnRadio, BTN.BACK, 20);            // Radio / MFD

        // D-Pad
        bindButton(elements.btnDup, BTN.DPAD_UP, 20);
        bindButton(elements.btnDdown, BTN.DPAD_DOWN, 20);
        bindButton(elements.btnDleft, BTN.DPAD_LEFT, 20);
        bindButton(elements.btnDright, BTN.DPAD_RIGHT, 20);

        // Face Buttons
        bindButton(elements.btnY, BTN.Y, 25); // DRS
        bindButton(elements.btnX, BTN.X, 20);
        bindButton(elements.btnA, BTN.A, 20);
        bindButton(elements.btnB, BTN.B, 25); // ERS

        // Mode Toggles
        if (elements.modeToggle) elements.modeToggle.addEventListener('click', toggleMode);
        if (elements.gamepadToggle) elements.gamepadToggle.addEventListener('click', toggleMode);

        // Center / Calibrate
        if (elements.btnCalibrate) {
            elements.btnCalibrate.addEventListener('click', function() {
                state.steerZeroOffset += state.steerAngleDeg;
                state.steerAngleDeg = 0;
                vibrate(50);
            });
        }

        // Fullscreen
        if (elements.btnFullscreen) {
            elements.btnFullscreen.addEventListener('click', function() {
                if (!document.fullscreenElement) document.documentElement.requestFullscreen().catch(console.error);
                else document.exitFullscreen().catch(console.error);
            });
        }

        startInputLoop();
        startTelemetryLoop();
    }

    document.addEventListener('DOMContentLoaded', init);
})();
