#!/bin/sh

echo "Executing $0 $1 $2 $3"
wine ResourceHacker.exe -extract "$1",foo.rc,,, 
sed -i -e "s/$2/$3/g" foo.rc
wine ResourceHacker.exe -delete "$1","$1",,,
wine ResourceHacker.exe -compile foo.rc,foo.res
wine ResourceHacker.exe -add "$1","$1",foo.res,,,

rm foo.rc
rm foo.rc-e
rm foo.res