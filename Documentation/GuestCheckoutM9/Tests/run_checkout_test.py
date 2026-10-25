from pathlib import Path
import subprocess,time,datetime,sys
P=Path(__file__).resolve().parents[3]
R=P/'Documentation/GuestCheckoutM9'/('run-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));R.mkdir()
app=P/'Builds/Playtest/MotelGame.app/Contents/MacOS/MotelGame';assert app.exists()
processes=[];observers=set()
def start(role):
 cmd=[str(app),'-batchmode','-nographics','-motel-checkout-test',role,'-motel-checkout-results',str(R),'-motel-character-report',str(R/(role+'-visuals.txt')),'-logFile',str(R/(role+'.log'))]
 if "--initial-only" in sys.argv:cmd.append("-motel-checkout-initial-only")
 proc=subprocess.Popen(cmd,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL);processes.append((role,proc));print('Started',role,proc.pid,flush=True);return proc
try:
 host=start('host');time.sleep(3);client=start('client');begin=time.monotonic();last=0
 while any(p.poll() is None for _,p in processes):
  if any(p.poll() not in [None,0] for _,p in processes):break
  for phase in ['returning','waiting','leaving']:
   if phase not in observers and (R/('observe-'+phase+'.signal')).exists():observers.add(phase);start('observer-'+phase)
  elapsed=time.monotonic()-begin
  if elapsed>1800:raise RuntimeError('M9 integration timeout')
  if elapsed-last>25:
   last=elapsed
   for role,p in processes:
    f=R/(role+'.progress')
    if p.poll() is None and f.exists():print(role+': '+f.read_text(),flush=True)
  time.sleep(1)
 for role,p in processes:
  f=R/(role+'.txt');print(role,'exit',p.poll(),f.read_text() if f.exists() else 'No report',flush=True)
 assert len(observers)==3 and all(p.poll()==0 for _,p in processes)
 assert all((R/(role+".txt")).exists() and (R/(role+".txt")).read_text().startswith("PASS "+role) for role,_ in processes), "Missing successful test report"
 for _,p in processes:p.wait()
finally:
 for _,p in processes:
  if p.poll() is None:p.terminate()
 print('Results:',R,flush=True)
