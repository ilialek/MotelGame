from pathlib import Path
import subprocess,time,datetime,os
P=next((q for q in Path(__file__).resolve().parents if (q/'Assets/Game').is_dir()),Path('/Users/ilalekomtsev/MotelGame'))
R=P/'Documentation/OnlineMultiplayerM13'/('lifecycle-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));R.mkdir(parents=True)
app=P/'Builds/Playtest/MotelGame.app/Contents/MacOS/MotelGame'; processes=[]; killed=False

def start(role):
    cmd=[str(app),'-motel-online-smoke-test',role,'-motel-online-results',str(R),'-motel-online','-motel-profile','lifecycle-host' if role=='duplicate' else 'lifecycle-'+role,'-logFile',str(R/(role+'.log'))]
    cmd+=['-screen-fullscreen','0','-screen-width','800','-screen-height','600'] if role in ['host','client1'] else ['-batchmode','-nographics']
    p=subprocess.Popen(cmd,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL);processes.append((role,p));print(role,p.pid,flush=True)
try:
    def wait_signal(name, limit=100):
        end=time.monotonic()+limit
        while not (R/(name+'.signal')).exists():
            if time.monotonic()>end or any(p.poll() not in [None,0] for _,p in processes):raise RuntimeError('Setup timeout/failure: '+name)
            time.sleep(1)
    start('host');wait_signal('code-one')
    start('duplicate');time.sleep(5)
    start('incompatible');wait_signal('incompatible-checked')
    for role in ['client1','client2','client3']:
        start(role);wait_signal(role+'-identity');time.sleep(3)
    wait_signal('full');start('fifth')
    begin=time.monotonic();last=0
    while any(p.poll() is None for _,p in processes):
        if (R/'kill-host.signal').exists() and not killed:
            host=next(p for role,p in processes if role=='host');host.kill();host.wait();killed=True;print('Host forcibly terminated after second session started',flush=True)
        if any(p.poll() not in [None,0] for role,p in processes if not (role=='host' and killed)):break
        elapsed=time.monotonic()-begin
        if elapsed>420:raise RuntimeError('Lifecycle test timeout')
        if elapsed-last>25:
            last=elapsed
            for role,p in processes:
                f=R/(role+'.progress')
                if p.poll() is None and f.exists():print(role+': '+f.read_text(),flush=True)
        time.sleep(1)
    for role,p in processes:
        f=R/('host-before-crash.txt' if role=='host' and killed else role+'.txt');print(role,'exit',p.poll(),f.read_text() if f.exists() else 'No report',flush=True)
    assert killed and all(p.poll()==0 and (R/(role+'.txt')).read_text().startswith('PASS') for role,p in processes if role!='host')
    (R/'result.txt').write_text('PASS: 4 distinct Relay players, fifth rejection, version rejection, spawn/ownership/movement, rejoin, graceful host leave, second session, forced host death.\n')
finally:
    for role,p in processes:
        if p.poll() is None:
            p.terminate()
            try:p.wait(timeout=8)
            except subprocess.TimeoutExpired:p.kill();p.wait()
    print('Results:',R,flush=True)
