import teller_sim as T, numpy as np, sys
T.SIGNED=True; T.SIGNED_ETA=0.6; T.GLOMS_PER_TAG=4; T.PRIOR_K=1.5
inter = sys.argv[1]=="combo"
kinds=sys.argv[2].split(",")
S=60
for k in kinds:
    A=[];B=[]
    for s in range(S):
        g,_,L,P=T.run(k,s,steps=120,interactions=inter); A.append(g)
        for t in P.h: P.h[t]=0.0
        itemsB=T.make_items(np.random.default_rng(s+9000),40,"B")
        g2,_,_,_=T.run(k,s+500,steps=40,player=P,items=itemsB,transfer=None if k=="oracle" else L); B.append(g2)
    A=np.array(A);B=np.array(B); se=lambda x:x.mean(1).std()/np.sqrt(S)
    print("%s|%s|%+.3f|%.3f|%+.3f|%.3f|%+.3f|%.3f|%+.3f"%(sys.argv[1],k,A[:,:30].mean(),se(A[:,:30]),A[:,30:].mean(),se(A[:,30:]),B[:,:20].mean(),se(B[:,:20]),B[:,20:].mean()),flush=True)
