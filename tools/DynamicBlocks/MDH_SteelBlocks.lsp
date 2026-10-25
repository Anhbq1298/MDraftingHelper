;;; MDraftingHeper - Dynamic Block template authoring for AutoCAD 2021.
;;;
;;; Each MDH-AUTHOR-* function runs INSIDE the block editor (-BEDIT <name>) and creates
;;; geometry plus constraint parameters. Constraint parameters (BCPARAMETER) are the
;;; values shown and edited in the native Properties palette (Ctrl+1) of a block reference.
;;;
;;; Constraint points are always given with an osnap prefix ("_endp of x,y"): the
;;; constraint commands reject plain typed coordinates and object picks in script mode.
;;; Geometric constraints come from AUTOCONSTRAIN on a selection set, which needs no picks.
;;; Authoring dimensions are deliberately distinct so AUTOCONSTRAIN cannot infer
;;; accidental equalities.

(vl-load-com)

;; Bit 1 = a command is still waiting for input (bit 4 = a script is running, always set here).
(defun mdh:busy () (= 1 (logand 1 (getvar "CMDACTIVE"))))

(defun mdh:p (x y) (strcat (rtos x 2 6) "," (rtos y 2 6)))
(defun mdh:endp (x y) (list "_endp" (mdh:p x y)))
(defun mdh:node (x y) (list "_node" (mdh:p x y)))

(defun mdh:line (a b)
  (entmakex (list '(0 . "LINE") '(8 . "0")
                  (list 10 (car a) (cadr a) 0.0) (list 11 (car b) (cadr b) 0.0))))

(defun mdh:arc (c r a0 a1)
  (entmakex (list '(0 . "ARC") '(8 . "0") (list 10 (car c) (cadr c) 0.0)
                  (cons 40 r) (cons 50 a0) (cons 51 a1))))

(defun mdh:origin-point ()
  (entmakex '((0 . "POINT") (8 . "0") (10 0.0 0.0 0.0))))

;; Dimensional constraint between two osnapped points. kind = "_Horizontal" / "_Vertical".
;; param = T creates a block constraint parameter (shown in Properties); NIL creates a
;; hidden dimensional constraint (driven by an expression).
(defun mdh:dim (param kind p1 p2 loc nameexpr)
  (command (if param "_.BCPARAMETER" "_.DIMCONSTRAINT") kind)
  (apply 'command p1)
  (apply 'command p2)
  (command (apply 'mdh:p loc) nameexpr)
  (if (mdh:busy)
    (progn (command) (princ (strcat "\nMDH-AUTHOR-ERROR constraint failed: " nameexpr)))
    (princ (strcat "\nMDH-AUTHOR-OK " nameexpr))))

(defun mdh:prepare ()
  (setvar "OSMODE" 0)
  (setvar "CONSTRAINTINFER" 0)
  (command "_.ZOOM" "_E")
  (command "_.ZOOM" "0.7x"))

(defun mdh:autoconstrain ()
  (command "_.AUTOCONSTRAIN" "_ALL" ""))

(defun mdh:fix-origin ()
  (mdh:origin-point)
  (command "_.GEOMCONSTRAINT" "_Fix")
  (apply 'command (mdh:node 0.0 0.0))
  (if (mdh:busy)
    (progn (command) (princ "\nMDH-AUTHOR-ERROR fix origin failed"))
    (princ "\nMDH-AUTHOR-OK fix origin")))

;;; ---------------------------------------------------------------- hollow sections

;; Rounded rectangle centred on the origin: half sizes hx, hy, corner radius rr.
(defun mdh:rrect (hx hy rr / cx cy)
  (setq cx (- hx rr) cy (- hy rr))
  (mdh:line (list (- cx) hy) (list cx hy))
  (mdh:line (list hx cy) (list hx (- cy)))
  (mdh:line (list cx (- hy)) (list (- cx) (- hy)))
  (mdh:line (list (- hx) (- cy)) (list (- hx) cy))
  (mdh:arc (list cx cy) rr 0.0 (/ pi 2))
  (mdh:arc (list (- cx) cy) rr (/ pi 2) pi)
  (mdh:arc (list (- cx) (- cy)) rr pi (* 1.5 pi))
  (mdh:arc (list cx (- cy)) rr (* 1.5 pi) (* 2 pi)))

;; RHS / SHS. r is the OUTER corner radius; the inner radius follows as r - t through
;; concentric + tangent constraints, so the wall thickness stays uniform.
(defun mdh:hollow-rect (square / h b tt ro hx hy cx cy)
  (setq h 300.0 b 200.0 tt 12.0 ro 30.0)
  (if square (setq h 220.0 b 220.0))
  (setq hx (/ b 2.0) hy (/ h 2.0) cx (- hx ro) cy (- hy ro))
  (mdh:rrect hx hy ro)
  (mdh:rrect (- hx tt) (- hy tt) (- ro tt))
  (mdh:prepare)
  (mdh:autoconstrain)
  (mdh:fix-origin)
  ;; Native Properties parameters
  (mdh:dim T "_Horizontal" (mdh:endp (- hx) cy) (mdh:endp hx cy) (list 0.0 (+ hy 60)) (strcat "B=" (rtos b 2 4)))
  (if square
    (mdh:dim nil "_Vertical" (mdh:endp cx hy) (mdh:endp cx (- hy)) (list (+ hx 50) 0.0) "hb=B")
    (mdh:dim T "_Vertical" (mdh:endp cx hy) (mdh:endp cx (- hy)) (list (+ hx 50) 0.0) (strcat "H=" (rtos h 2 4))))
  (mdh:dim T "_Horizontal" (mdh:endp hx cy) (mdh:endp (- hx tt) cy) (list (- hx (/ tt 2)) (+ hy 30)) (strcat "t=" (rtos tt 2 4)))
  ;; For a 90 degree corner arc, the horizontal distance between its end points is its radius.
  (mdh:dim T "_Horizontal" (mdh:endp cx hy) (mdh:endp hx cy) (list (+ cx (/ ro 2)) (+ hy 90)) (strcat "r=" (rtos ro 2 4)))
  ;; Hidden constraints: other corners equal r, and the origin at the centroid.
  (mdh:dim nil "_Horizontal" (mdh:endp (- cx) hy) (mdh:endp (- hx) cy) (list (- (+ cx (/ ro 2))) (+ hy 90)) "r2=r")
  (mdh:dim nil "_Horizontal" (mdh:endp (- hx) (- cy)) (mdh:endp (- cx) (- hy)) (list (- (+ cx (/ ro 2))) (- (+ hy 40))) "r3=r")
  (mdh:dim nil "_Horizontal" (mdh:endp cx (- hy)) (mdh:endp hx (- cy)) (list (+ cx (/ ro 2)) (- (+ hy 40))) "r4=r")
  (mdh:dim nil "_Horizontal" (mdh:node 0.0 0.0) (mdh:endp hx cy) (list (/ hx 2) (- (/ hy 3))) "ox=B/2")
  (mdh:dim nil "_Vertical" (mdh:node 0.0 0.0) (mdh:endp cx hy) (list (- (/ hx 3)) (/ hy 2)) (if square "oy=B/2" "oy=H/2"))
  (princ))

(defun mdh-author-rhs () (mdh:hollow-rect nil))
(defun mdh-author-shs () (mdh:hollow-rect T))

;; CHS as two semicircular arcs per loop, so the loops have end points that point-based
;; constraint parameters can reference.
(defun mdh-author-chs (/ ro ri)
  (setq ro 100.0 ri 80.0)
  (mdh:arc '(0.0 0.0) ro 0.0 pi)
  (mdh:arc '(0.0 0.0) ro pi (* 2 pi))
  (mdh:arc '(0.0 0.0) ri 0.0 pi)
  (mdh:arc '(0.0 0.0) ri pi (* 2 pi))
  (mdh:prepare)
  (mdh:autoconstrain)
  (mdh:fix-origin)
  ;; Pin the common centre to the origin; otherwise D can be satisfied by a chord.
  (command "_.GEOMCONSTRAINT" "_Coincident")
  (apply 'command (mdh:node 0.0 0.0))
  (command "_cen" (mdh:p ro 0.0))
  (if (mdh:busy)
    (progn (command) (princ "\nMDH-AUTHOR-ERROR chs centre"))
    (princ "\nMDH-AUTHOR-OK chs centre"))
  (mdh:dim T "_Horizontal" (mdh:endp (- ro) 0.0) (mdh:endp ro 0.0) (list 0.0 (+ ro 50)) (strcat "D=" (rtos (* 2 ro) 2 4)))
  (mdh:dim T "_Horizontal" (mdh:endp ro 0.0) (mdh:endp ri 0.0) (list (/ (+ ro ri) 2) (+ ro 25)) (strcat "t=" (rtos (- ro ri) 2 4)))
  (mdh:dim nil "_Horizontal" (mdh:node 0.0 0.0) (mdh:endp ro 0.0) (list (/ ro 2) (- (/ ro 3))) "ox=D/2")
  ;; Keep the right-hand junction on the horizontal axis through the origin.
  (command "_.GEOMCONSTRAINT" "_Horizontal" "_2Points")
  (apply 'command (mdh:node 0.0 0.0))
  (apply 'command (mdh:endp ro 0.0))
  (if (mdh:busy)
    (progn (command) (princ "\nMDH-AUTHOR-ERROR chs axis"))
    (princ "\nMDH-AUTHOR-OK chs axis"))
  (princ))

;;; ---------------------------------------------------------------- I-section (UB / UC)

(defun mdh-author-isection (/ h b tw tf r x1 y1 y2 xw xf yf)
  (setq h 400.0 b 200.0 tw 20.0 tf 30.0 r 24.0)
  (setq x1 (/ b 2.0) y1 (/ h 2.0) y2 (- y1 tf) xw (/ tw 2.0) xf (+ xw r) yf (- y2 r))
  ;; Outline, counter-clockwise from the top-left corner.
  (mdh:line (list (- x1) y1) (list x1 y1))
  (mdh:line (list x1 y1) (list x1 y2))
  (mdh:line (list x1 y2) (list xf y2))
  (mdh:arc (list xf yf) r (/ pi 2) pi)
  (mdh:line (list xw yf) (list xw (- yf)))
  (mdh:arc (list xf (- yf)) r pi (* 1.5 pi))
  (mdh:line (list xf (- y2)) (list x1 (- y2)))
  (mdh:line (list x1 (- y2)) (list x1 (- y1)))
  (mdh:line (list x1 (- y1)) (list (- x1) (- y1)))
  (mdh:line (list (- x1) (- y1)) (list (- x1) (- y2)))
  (mdh:line (list (- x1) (- y2)) (list (- xf) (- y2)))
  (mdh:arc (list (- xf) (- yf)) r (* 1.5 pi) (* 2 pi))
  (mdh:line (list (- xw) (- yf)) (list (- xw) yf))
  (mdh:arc (list (- xf) yf) r 0.0 (/ pi 2))
  (mdh:line (list (- xf) y2) (list (- x1) y2))
  (mdh:line (list (- x1) y2) (list (- x1) y1))
  (mdh:prepare)
  (mdh:autoconstrain)
  (mdh:fix-origin)
  (mdh:dim T "_Vertical" (mdh:endp x1 y1) (mdh:endp x1 (- y1)) (list (+ x1 80) 0.0) (strcat "H=" (rtos h 2 4)))
  (mdh:dim T "_Horizontal" (mdh:endp (- x1) y1) (mdh:endp x1 y1) (list 0.0 (+ y1 70)) (strcat "B=" (rtos b 2 4)))
  (mdh:dim T "_Horizontal" (mdh:endp (- xw) yf) (mdh:endp xw yf) (list 0.0 (/ yf 2)) (strcat "tw=" (rtos tw 2 4)))
  (mdh:dim T "_Vertical" (mdh:endp x1 y1) (mdh:endp x1 y2) (list (+ x1 40) (- y1 (/ tf 2))) (strcat "tf=" (rtos tf 2 4)))
  (mdh:dim T "_Horizontal" (mdh:endp xf y2) (mdh:endp xw yf) (list (+ xw (/ r 2)) (+ y1 35)) (strcat "r=" (rtos r 2 4)))
  ;; Hidden: the other flange tips equal tf, the other fillets equal r, the bottom flange
  ;; equals B, and the origin sits at the centroid of the doubly symmetric section.
  (mdh:dim nil "_Vertical" (mdh:endp (- x1) y1) (mdh:endp (- x1) y2) (list (- (+ x1 40)) (- y1 (/ tf 2))) "tf2=tf")
  (mdh:dim nil "_Vertical" (mdh:endp x1 (- y1)) (mdh:endp x1 (- y2)) (list (+ x1 40) (- (- y1 (/ tf 2)))) "tf3=tf")
  (mdh:dim nil "_Vertical" (mdh:endp (- x1) (- y1)) (mdh:endp (- x1) (- y2)) (list (- (+ x1 40)) (- (- y1 (/ tf 2)))) "tf4=tf")
  (mdh:dim nil "_Horizontal" (mdh:endp xf (- y2)) (mdh:endp xw (- yf)) (list (+ xw (/ r 2)) (- (+ y1 35))) "r2=r")
  (mdh:dim nil "_Horizontal" (mdh:endp (- xf) (- y2)) (mdh:endp (- xw) (- yf)) (list (- (+ xw (/ r 2))) (- (+ y1 35))) "r3=r")
  (mdh:dim nil "_Horizontal" (mdh:endp (- xf) y2) (mdh:endp (- xw) yf) (list (- (+ xw (/ r 2))) (+ y1 35)) "r4=r")
  (mdh:dim nil "_Horizontal" (mdh:endp (- x1) (- y1)) (mdh:endp x1 (- y1)) (list 0.0 (- (+ y1 70))) "bb=B")
  (mdh:dim nil "_Horizontal" (mdh:node 0.0 0.0) (mdh:endp x1 y1) (list (/ x1 2) (/ y1 3)) "ox=B/2")
  (mdh:dim nil "_Vertical" (mdh:node 0.0 0.0) (mdh:endp x1 y1) (list (/ x1 3) (/ y1 2)) "oy=H/2")
  (mdh:dim nil "_Horizontal" (mdh:node 0.0 0.0) (mdh:endp xw yf) (list (/ xw 2) (- (/ yf 2))) "ow=tw/2")
  (princ))

;;; ---------------------------------------------------------------- finishing

;; Marks the authored block definition: millimetre units, explodable, uniform scale off.
(defun mdh-finish-block (name / blk)
  (setq blk (vla-Item (vla-get-Blocks (vla-get-ActiveDocument (vlax-get-acad-object))) name))
  (vla-put-Units blk acInsertUnitsMillimeters)
  (vla-put-Comments blk (strcat "MDraftingHeper steel section template " name " (schema 1)"))
  (princ (strcat "\nMDH-AUTHOR-OK finished " name)))
