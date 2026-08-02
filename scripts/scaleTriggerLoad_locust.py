"""
scaleTriggerLoad_locust.py

Locust load-test script for the ScaleTrigger API.

Sends POST /api/vote/add?option=yes|no against a real, external URL of
the application, randomly choosing yes/no per request (50/50). Works
both for local runs (`locust -f ...`) and as a direct upload to Azure
Load Testing - Azure Load Testing runs Locust scripts natively, no
changes needed to this file.

Authentication: on_start() runs once per simulated user and sends a
single probe vote with no Authorization header. If the API responds
401 Unauthorized (i.e. Auth:Enabled=true on the API side), the user
logs in with admin:admin (the API's default AdminUser:Username /
AdminUser:Password - override via the AUTH_USERNAME / AUTH_PASSWORD
environment variables if you changed them) and attaches the resulting
JWT as a Bearer token on every subsequent vote request. If the API
does not require auth, the user proceeds with no Authorization header.
Note: the probe itself sends a real vote if auth is disabled, same as
the original scaleTriggerLoad.py.

Running locally (web UI):

    pip install locust
    locust -f scaleTriggerLoad_locust.py --host https://scaletrigger-api.azurewebsites.net

Then open http://localhost:8089 and set the number of users and spawn
rate - Locust's built-in ramp-up, equivalent to scaleTriggerLoad.py's
--ramp/--ramp-step/--ramp-interval.

Running locally (headless):

    locust -f scaleTriggerLoad_locust.py \
        --host https://scaletrigger-api.azurewebsites.net \
        --users 200 --spawn-rate 10 --run-time 5m --headless

Running on Azure Load Testing:

    Create a test in the Azure portal (or via CLI/YAML config), choose
    "Locust" as the test type, and upload this file as the test
    script. Set the target host, user count, spawn rate and duration in
    the Azure Load Testing test configuration instead of on the command
    line - everything else works unchanged.

Note on "votes per second" vs "users": Locust ramps by number of
concurrent simulated users, not a direct requests-per-second target
like scaleTriggerLoad.py's --votes. With wait_time set to 0 (see
below), each user fires votes back-to-back, so users roughly
correspond to concurrent in-flight requests - tune --users/--spawn-rate
against the observed request rate rather than assuming a 1:1 mapping.

Environment variables:

    AUTH_USERNAME / AUTH_PASSWORD   Override the admin:admin default
                                     used for JWT login when the API
                                     requires auth.
"""

import os
import random

from locust import HttpUser, task, between

AUTH_USERNAME = os.environ.get("AUTH_USERNAME", "admin")
AUTH_PASSWORD = os.environ.get("AUTH_PASSWORD", "admin")


class ScaleTriggerVoter(HttpUser):
    # No think time between votes by default - ScaleTrigger's purpose is
    # to be hammered as fast as the configured user/spawn rate allows.
    # Set a real range (e.g. between(0.1, 0.5)) to simulate more
    # human-like pacing between votes instead.
    wait_time = between(0, 0)

    token = None

    def on_start(self):
        """Probe once per simulated user: try an unauthenticated vote
        first, log in only if the API answers 401 (Auth:Enabled=true)."""
        probe = self.client.post("/api/vote/add?option=yes", name="/api/vote/add [probe]")

        if probe.status_code == 401:
            login = self.client.post(
                "/api/auth/login",
                json={"username": AUTH_USERNAME, "password": AUTH_PASSWORD},
                name="/api/auth/login",
            )
            login.raise_for_status()
            self.token = login.json()["token"]

    @task
    def vote(self):
        option = random.choice(["yes", "no"])
        headers = {"Authorization": f"Bearer {self.token}"} if self.token else {}
        self.client.post(f"/api/vote/add?option={option}", headers=headers, name="/api/vote/add")
