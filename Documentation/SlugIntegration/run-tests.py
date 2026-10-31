import os,sys,json,subprocess,time
from pathlib import Path
P=Path(__file__).resolve().parents[2]
if len(sys.argv) != 3: raise SystemExit('Usage: python3 Documentation/SlugIntegration/run-tests.py MODE UNIQUE_RUN_NAME')
mode=sys.argv[1];run=sys.argv[2]
assert mode in ['underground-hunt','underground-routes','registration','service']
out=P/'Builds/SlugIntegration'/('test-'+mode+'-'+run)
assert not out.exists();out.mkdir()
pointer=P/'Builds/SlugIntegration/latest-build-path.txt'
app=Path(pointer.read_text()) if pointer.exists() else P/'Builds/SlugIntegration/MotelGame.app'
exe=app/'Contents/MacOS/MotelGame'
assert exe.exists()
processes=[];started=time.monotonic();last={};late=False
def launch(role):
 args=[str(exe),'-batchmode','-nographics','-motel-layout-test',role,'-motel-layout-mode',mode,'-motel-layout-results',str(out),'-motel-profile','slug-'+mode+'-'+role+'-'+run,'-logFile',str(out/(role+'.log'))]
 if mode=='underground-hunt':args+=['-motel-slug-validation','yes']
 p=subprocess.Popen(args,cwd=P,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 processes.append((role,p));print('START',mode,role,p.pid,flush=True)
try:
 launch('host');time.sleep(3);launch('client')
 while any(p.poll() is None for _,p in processes):
  if (out/'late.signal').exists() and not late:late=True;launch('late')
  for role,p in processes:
   q=out/(role+'.progress')
   if q.exists():
    content=q.read_text()
    if content!=last.get(role):print('PROGRESS',role,content,flush=True);last[role]=content
   report=out/(role+'.txt')
   if p.poll() not in (None,0) or (report.exists() and report.read_text().startswith('FAIL')):
    raise RuntimeError(role+': '+(report.read_text() if report.exists() else 'exit '+str(p.poll())))
  if time.monotonic()-started>1200:raise RuntimeError('1200s timeout')
  time.sleep(2)
 reports={}
 for role,p in processes:
  q=out/(role+'.txt');content=q.read_text() if q.exists() else ''
  reports[role]={'exit_code':p.returncode,'passed':content.startswith('PASS'),'report':str(q)}
 passed=all(r['passed'] and r['exit_code']==0 for r in reports.values())
 result={'passed':passed,'mode':mode,'late_join':late,'reports':reports,'seconds':round(time.monotonic()-started,1)}
 (out/'result.json').write_text(json.dumps(result,indent=2));print('RESULT',json.dumps(result),flush=True)
 assert passed
finally:
 for role,p in processes:
  if p.poll() is None:
   p.terminate()
   try:p.wait(timeout=10)
   except subprocess.TimeoutExpired:p.kill();p.wait()
