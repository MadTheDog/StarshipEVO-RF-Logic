using System;
using RadioLogic;
int checks=0;
void Check(bool result,string label){checks++;if(!result)throw new Exception(label);}
int[] expected={25,50,125,500,2000};
for(int i=1;i<=5;i++)
{
 Check(RadioPolicy.Range(i)==expected[i-1],"agreed level "+i);
 Check(RadioPolicy.Reach(i,i,expected[i-1]),"inclusive boundary "+i);
 Check(!RadioPolicy.Reach(i,i,expected[i-1]+.001),"outside boundary "+i);
 for(int j=1;j<=5;j++)
 {
  int r=Math.Min(expected[i-1],expected[j-1]);
  Check(RadioPolicy.Reach(i,j,r),"mutual reach");
  Check(!RadioPolicy.Reach(i,j,r+.001),"shorter antenna limits range");
 }
}
foreach(double d in new[]{double.NaN,double.PositiveInfinity,-1})Check(!RadioPolicy.Reach(5,5,d),"invalid distance");
foreach(int level in new[]{0,-1,6,255})Check(!RadioPolicy.Reach(level,5,0),"invalid level");
Check(RadioPolicy.IsBinding(true,false,false,true),"TX to antenna is an RF binding");
Check(RadioPolicy.IsBinding(false,true,true,false),"antenna to RX is an RF binding");
Check(!RadioPolicy.IsBinding(false,false,false,true),"ordinary antenna enable input is not a payload binding");
Check(!RadioPolicy.IsBinding(true,false,true,false),"TX to RX is not a local antenna binding");
Check(!RadioPolicy.IsBinding(false,true,false,true),"antenna to antenna is not a binding");
Check(RadioPolicy.CanPair(true,false,true,true,false),"eligible unpaired endpoints can be paired");
Check(!RadioPolicy.CanPair(true,true,true,true,false),"same role cannot pair");
Check(!RadioPolicy.CanPair(true,false,false,true,false),"same grid cannot pair");
Check(!RadioPolicy.CanPair(true,false,true,false,false),"ineligible grids cannot pair");
Check(!RadioPolicy.CanPair(true,false,true,true,true),"pairing must not steal an occupied endpoint");
Console.WriteLine(checks+" RF policy checks passed.");
