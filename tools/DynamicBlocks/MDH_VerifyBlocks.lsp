;;; MDraftingHeper - in-host verification of the steel Dynamic Block templates.
;;; Prints "MDH-VERIFY PASS|FAIL <check>" lines into the command history log.

(vl-load-com)

(setq mdh:tol 1e-3)
(defun mdh:near (a b) (< (abs (- a b)) mdh:tol))
(defun mdh:check (name ok detail)
  (princ (strcat "\nMDH-VERIFY " (if ok "PASS " "FAIL ") name (if ok "" (strcat " :: " detail))))
  ok)

(defun mdh:space () (vla-get-ModelSpace (vla-get-ActiveDocument (vlax-get-acad-object))))

(defun mdh:insert (name x y)
  (vla-InsertBlock (mdh:space) (vlax-3d-point (list x y 0.0)) name 1.0 1.0 1.0 0.0))

(defun mdh:props (br / out)
  (foreach p (vlax-invoke br 'GetDynamicBlockProperties)
    (if (= (vla-get-Show p) :vlax-true)
      (setq out (cons (cons (vla-get-PropertyName p) p) out))))
  (reverse out))

(defun mdh:get (br name / p)
  (if (setq p (cdr (assoc name (mdh:props br)))) (vlax-variant-value (vla-get-Value p))))

(defun mdh:set (br name val / p)
  (if (setq p (cdr (assoc name (mdh:props br))))
    (vl-catch-all-apply 'vla-put-Value (list p (vlax-make-variant val vlax-vbDouble)))))

;; Reads the geometry the reference currently displays (its anonymous block definition)
;; without modifying the drawing, so checks do not create undo records.
;; Returns (lines arcs): lines = ((x1 y1 x2 y2)...), arcs = ((cx cy r)...) in block coordinates.
(defun mdh:geom (br / e d lines arcs sp ep c)
  (setq e (cdr (assoc -2 (entget (tblobjname "BLOCK" (vla-get-Name br))))))
  (while e
    (setq d (entget e))
    (cond ((= (cdr (assoc 0 d)) "LINE")
           (setq sp (cdr (assoc 10 d)) ep (cdr (assoc 11 d)))
           (setq lines (cons (list (car sp) (cadr sp) (car ep) (cadr ep)) lines)))
          ((= (cdr (assoc 0 d)) "ARC")
           (setq c (cdr (assoc 10 d)))
           (setq arcs (cons (list (car c) (cadr c) (cdr (assoc 40 d))) arcs))))
    (setq e (entnext e)))
  (list lines arcs))

(defun mdh:bbox (g / xs ys)
  (foreach l (car g) (setq xs (append (list (nth 0 l) (nth 2 l)) xs) ys (append (list (nth 1 l) (nth 3 l)) ys)))
  (foreach a (cadr g)
    (setq xs (append (list (- (car a) (caddr a)) (+ (car a) (caddr a))) xs)
          ys (append (list (- (cadr a) (caddr a)) (+ (cadr a) (caddr a))) ys)))
  (list (apply 'min xs) (apply 'min ys) (apply 'max xs) (apply 'max ys)))

(defun mdh:radii (g) (mapcar 'caddr (cadr g)))
(defun mdh:all (pred lst / ok) (setq ok T) (foreach x lst (if (not (apply pred (list x))) (setq ok nil))) ok)
(defun mdh:count (pred lst / n) (setq n 0) (foreach x lst (if (apply pred (list x)) (setq n (1+ n)))) n)
(defun mdh:fmt (lst) (vl-princ-to-string (mapcar '(lambda (v) (if (numberp v) (/ (fix (* v 1000.0)) 1000.0) v)) lst)))

(defun mdh:check-box (tag g hx hy / bb)
  (setq bb (mdh:bbox g))
  (mdh:check (strcat tag " extents")
             (and (mdh:near (nth 0 bb) (- hx)) (mdh:near (nth 1 bb) (- hy)) (mdh:near (nth 2 bb) hx) (mdh:near (nth 3 bb) hy))
             (mdh:fmt bb)))

;; Vertical lines at |x| = v (count n)
(defun mdh:vlines-at (g v)
  (mdh:count '(lambda (l) (and (mdh:near (nth 0 l) (nth 2 l)) (mdh:near (abs (nth 0 l)) v))) (car g)))
(defun mdh:hlines-at (g v)
  (mdh:count '(lambda (l) (and (mdh:near (nth 1 l) (nth 3 l)) (mdh:near (abs (nth 1 l)) v))) (car g)))

;;; ------------------------------------------------------------ staged checks
;;; Constraint parameters are re-evaluated when the calling command ends, so values are
;;; set in one script line and geometry is checked in a later line.

(setq *mdh:cases* nil)
(defun mdh:case (tag kind br vals) (setq *mdh:cases* (cons (list tag kind br vals) *mdh:cases*)))
(defun mdh:case-br (tag) (caddr (assoc tag *mdh:cases*)))
(defun mdh:case-vals (tag) (cadddr (assoc tag *mdh:cases*)))
(defun mdh:val (tag key) (cdr (assoc key (mdh:case-vals tag))))

(defun mdh:apply-vals (br vals) (foreach kv vals (mdh:set br (car kv) (cdr kv))))

(defun mdh:new-case (tag kind block x vals / br)
  (setq br (mdh:insert block x 0.0))
  (mdh:apply-vals br vals)
  (mdh:case tag kind br vals)
  br)

;; Stage 1: insert and set values.
(defun mdh-verify-set (/ shs)
  (setq *mdh:cases* nil)
  (mdh:new-case "CHS" 'chs "MDH_CHS" 0.0 '(("D" . 168.3) ("t" . 7.1)))
  (setq shs (mdh:new-case "SHS" 'shs "MDH_SHS" 1000.0 '(("B" . 150.0) ("t" . 8.0) ("r" . 16.0))))
  (mdh:new-case "RHS" 'rhs "MDH_RHS" 2000.0 '(("B" . 150.0) ("H" . 250.0) ("t" . 10.0) ("r" . 20.0)))
  (mdh:new-case "UB" 'i "MDH_ISECTION" 3000.0 '(("H" . 406.6) ("B" . 178.0) ("tw" . 7.7) ("tf" . 10.9) ("r" . 10.2)))
  (mdh:new-case "UC" 'i "MDH_ISECTION" 4000.0 '(("H" . 203.2) ("B" . 203.6) ("tw" . 7.2) ("tf" . 11.0) ("r" . 10.2)))
  (mdh:new-case "RHS-default" 'rhs "MDH_RHS" 5000.0 nil)
  (mdh:new-case "SHS-copy" 'shs nil 0.0 nil)
  (princ "\nMDH-VERIFY STAGE set")
  (princ))

;; mdh:new-case with block nil is replaced here by a copy of the SHS instance.
(defun mdh:insert (name x y / src)
  (if name
    (vla-InsertBlock (mdh:space) (vlax-3d-point (list x y 0.0)) name 1.0 1.0 1.0 0.0)
    (progn (setq src (mdh:case-br "SHS"))
           (setq src (vla-Copy src))
           (vla-Move src (vlax-3d-point '(0.0 0.0 0.0)) (vlax-3d-point '(0.0 1000.0 0.0)))
           src)))

(defun mdh:check-case (tag / kind br v g d tt b h r tw tf)
  (setq kind (cadr (assoc tag *mdh:cases*)) br (mdh:case-br tag))
  (setq v '(lambda (k) (mdh:get br k)))
  (setq g (mdh:geom br))
  (cond
    ((= kind 'chs)
     (setq d (mdh:get br "D") tt (mdh:get br "t"))
     (mdh:check-box tag g (/ d 2.0) (/ d 2.0))
     (mdh:check (strcat tag " concentric at origin") (mdh:all '(lambda (a) (and (mdh:near (car a) 0.0) (mdh:near (cadr a) 0.0))) (cadr g)) (mdh:fmt (cadr g)))
     (mdh:check (strcat tag " radii D/2 and D/2-t")
                (and (= 2 (mdh:count '(lambda (x) (mdh:near x (/ d 2.0))) (mdh:radii g)))
                     (= 2 (mdh:count '(lambda (x) (mdh:near x (- (/ d 2.0) tt))) (mdh:radii g))))
                (mdh:fmt (mdh:radii g))))
    ((member kind '(shs rhs))
     (setq b (mdh:get br "B") tt (mdh:get br "t") r (mdh:get br "r"))
     (setq h (if (= kind 'shs) b (mdh:get br "H")))
     (mdh:check-box tag g (/ b 2.0) (/ h 2.0))
     (mdh:check (strcat tag " outer radii = r") (= 4 (mdh:count '(lambda (x) (mdh:near x r)) (mdh:radii g))) (mdh:fmt (mdh:radii g)))
     (mdh:check (strcat tag " inner radii = r - t") (= 4 (mdh:count '(lambda (x) (mdh:near x (- r tt))) (mdh:radii g))) (mdh:fmt (mdh:radii g)))
     (mdh:check (strcat tag " wall verticals") (and (= 2 (mdh:vlines-at g (/ b 2.0))) (= 2 (mdh:vlines-at g (- (/ b 2.0) tt)))) (mdh:fmt (car g)))
     (mdh:check (strcat tag " wall horizontals") (and (= 2 (mdh:hlines-at g (/ h 2.0))) (= 2 (mdh:hlines-at g (- (/ h 2.0) tt)))) ""))
    ((= kind 'i)
     (setq h (mdh:get br "H") b (mdh:get br "B") tw (mdh:get br "tw") tf (mdh:get br "tf") r (mdh:get br "r"))
     (mdh:check-box tag g (/ b 2.0) (/ h 2.0))
     (mdh:check (strcat tag " root radii") (and (= 4 (length (cadr g))) (mdh:all '(lambda (x) (mdh:near x r)) (mdh:radii g))) (mdh:fmt (mdh:radii g)))
     (mdh:check (strcat tag " web faces") (= 2 (mdh:vlines-at g (/ tw 2.0))) (mdh:fmt (car g)))
     (mdh:check (strcat tag " flange undersides") (= 4 (mdh:hlines-at g (- (/ h 2.0) tf))) "")
     (mdh:check (strcat tag " fillet centres")
                (mdh:all '(lambda (a) (and (mdh:near (abs (car a)) (+ (/ tw 2.0) r)) (mdh:near (abs (cadr a)) (- (/ h 2.0) tf r)))) (cadr g))
                (mdh:fmt (cadr g))))))

(defun mdh:expect-values (tag / br ok)
  (setq br (mdh:case-br tag) ok T)
  (foreach kv (mdh:case-vals tag) (if (not (mdh:near (mdh:get br (car kv)) (cdr kv))) (setq ok nil)))
  (mdh:check (strcat tag " property values") ok
             (vl-princ-to-string (mapcar '(lambda (kv) (cons (car kv) (mdh:get br (car kv)))) (mdh:case-vals tag)))))

;; Stage 2: geometry follows the parameters; instances are independent.
(defun mdh-verify-check ()
  (foreach tag '("CHS" "SHS" "RHS" "UB" "UC")
    (mdh:expect-values tag)
    (mdh:check-case tag))
  (mdh:check "RHS untouched instance keeps template values"
             (and (mdh:near (mdh:get (mdh:case-br "RHS-default") "B") 200.0) (mdh:near (mdh:get (mdh:case-br "RHS-default") "H") 300.0))
             (mdh:fmt (list (mdh:get (mdh:case-br "RHS-default") "B") (mdh:get (mdh:case-br "RHS-default") "H"))))
  (mdh:check-case "RHS-default")
  (princ "\nMDH-VERIFY STAGE check")
  (princ))

;; Stage 3: edit the copy only, then (separate line) check both.
(defun mdh-verify-copy-set () (mdh:set (mdh:case-br "SHS-copy") "B" 260.0) (princ))
(defun mdh-verify-copy-check ()
  (mdh:check "copied instance edited independently"
             (and (mdh:near (mdh:get (mdh:case-br "SHS-copy") "B") 260.0) (mdh:near (mdh:get (mdh:case-br "SHS") "B") 150.0))
             (mdh:fmt (list (mdh:get (mdh:case-br "SHS-copy") "B") (mdh:get (mdh:case-br "SHS") "B"))))
  (mdh:check-case "SHS-copy")
  (mdh:check-case "SHS")
  (princ))

;; Stage 4: undo / redo of a parameter edit (driven by U and REDO script lines).
(defun mdh-verify-undo-set ()
  (command "_.UNDO" "_Begin")
  (mdh:set (mdh:case-br "RHS") "B" 300.0)
  (command "_.UNDO" "_End")
  (princ))
(defun mdh-verify-undo-check (expectB label / br)
  (setq br (mdh:case-br "RHS"))
  (mdh:check (strcat "RHS " label " B=" (rtos expectB 2 1)) (mdh:near (mdh:get br "B") expectB) (mdh:fmt (list (mdh:get br "B"))))
  (mdh:check-case "RHS")
  (princ))

;; Stage 5: an impossible wall thickness. Records what AutoCAD does.
(defun mdh-verify-invalid-set () (setq *mdh:bad* (mdh:case-br "RHS-default")) (princ (strcat "\nMDH-VERIFY INFO set t=150 result=" (vl-princ-to-string (mdh:set *mdh:bad* "t" 150.0)))) (princ))
(defun mdh-verify-invalid-check (/ g)
  (setq g (mdh:geom *mdh:bad*))
  (princ (strcat "\nMDH-VERIFY INFO after t=150 on B=200: t=" (vl-princ-to-string (mdh:get *mdh:bad* "t"))
                 " radii=" (mdh:fmt (mdh:radii g)) " extents=" (mdh:fmt (mdh:bbox g))))
  (princ "\nMDH-VERIFY DONE")
  (princ))
